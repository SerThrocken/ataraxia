using System.Security.Cryptography;
using System.Text;
using Hermes.Client.Core;
using Renci.SshNet;

namespace Hermes.Client.Infrastructure;

/// <summary>
/// <see cref="ITerminalSession"/> over SSH.NET: an interactive shell channel
/// (<c>xterm-256color</c>) to a saved <see cref="SshHost"/>. Private keys and
/// passwords are read from <see cref="ISecretStore"/> (DPAPI) at connect time
/// and never persisted by this class — they live only in the SSH.NET auth
/// objects for the session's lifetime.
/// </summary>
public sealed class SshNetTerminalSession : ITerminalSession
{
    private const string TerminalType = "xterm-256color";

    private readonly SshHost _host;
    private readonly ISecretStore _secrets;
    private readonly ISshHostKeyTrust _trust;

    private SshClient? _client;
    private ShellStream? _shell;
    private CancellationTokenSource? _pumpCts;
    private Task? _pump;
    private bool _disconnectedFired;
    private bool _disposed;

    public SshNetTerminalSession(SshHost host, ISecretStore secrets, ISshHostKeyTrust trust)
    {
        _host = host;
        _secrets = secrets;
        _trust = trust;
    }

    public string Title => _host.Name;

    public bool IsConnected => _client?.IsConnected == true;

    public event EventHandler<TerminalDataEventArgs>? DataReceived;

    public event EventHandler? Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected)
        {
            return;
        }

        var options = SshConnectionOptions.FromHost(_host);
        var invalid = options.Validate();
        if (invalid is not null)
        {
            throw new InvalidOperationException(invalid);
        }

        var auth = await BuildAuthMethodsAsync(cancellationToken).ConfigureAwait(false);
        var connectionInfo = new ConnectionInfo(
            _host.Host.Trim(), _host.Port, _host.Username.Trim(), auth);

        var pinned = await _trust.GetPinnedFingerprintAsync(
            _host.Host, _host.Port, cancellationToken).ConfigureAwait(false);
        string? presented = null;
        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, e) =>
        {
            var fingerprint = Fingerprint(e.HostKey);
            if (string.Equals(fingerprint, pinned, StringComparison.Ordinal))
            {
                e.CanTrust = true;
            }
            else
            {
                // Don't trust yet: surface the presented key to the UI via
                // SshUnknownHostKeyException so the user can approve and pin it.
                presented = fingerprint;
                e.CanTrust = false;
            }
        };

        try
        {
            await Task.Run(() => client.Connect(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (presented is not null && !client.IsConnected)
        {
            client.Dispose();
            throw new SshUnknownHostKeyException(
                _host.Host.Trim(), _host.Port, presented, keyChanged: pinned is not null, ex);
        }

        _client = client;
        try
        {
            _shell = client.CreateShellStream(TerminalType, 80, 24, 0, 0, 1024);
        }
        catch
        {
            // Shell setup failed after connect: don't leak the transport.
            _client = null;
            client.Dispose();
            throw;
        }

        _pumpCts = new CancellationTokenSource();
        _pump = Task.Run(() => PumpAsync(_pumpCts.Token), CancellationToken.None);
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);
        var shell = _shell ?? throw new InvalidOperationException("Not connected.");
        await shell.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
        await shell.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        _shell?.ChangeWindowSize((uint)columns, (uint)rows, (uint)columns, (uint)rows);
        return Task.CompletedTask;
    }

    public async Task CloseAsync() => await DisposeAsync().ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pumpCts?.Cancel();

        if (_pump is not null)
        {
            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The pump already reported the failure via Disconnected;
                // teardown must not throw.
            }
        }

        _pumpCts?.Dispose();
        _shell?.Dispose();
        _client?.Dispose();
        FireDisconnected();
    }

    private async Task<AuthenticationMethod[]> BuildAuthMethodsAsync(
        CancellationToken cancellationToken)
    {
        var username = _host.Username.Trim();
        if (_host.AuthKind == SshAuthKind.Password)
        {
            var password = await _secrets.GetAsync(_host.PasswordAlias, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"No password stored for SSH host '{_host.Name}'.");
            return new AuthenticationMethod[]
                { new PasswordAuthenticationMethod(username, password) };
        }

        var keyText = await _secrets.GetAsync(_host.PrivateKeyAlias, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No private key stored for SSH host '{_host.Name}'.");
        var passphrase = await _secrets.GetAsync(_host.KeyPassphraseAlias, cancellationToken)
            .ConfigureAwait(false);
        var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(keyText));
        var keyFile = string.IsNullOrEmpty(passphrase)
            ? new PrivateKeyFile(keyStream)
            : new PrivateKeyFile(keyStream, passphrase);
        return new AuthenticationMethod[]
            { new PrivateKeyAuthenticationMethod(username, keyFile) };
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _shell is { } shell)
            {
                var read = await shell.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                DataReceived?.Invoke(this, new TerminalDataEventArgs(chunk));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on CloseAsync.
        }
        catch (Exception)
        {
            // Transport dropped mid-session; the Disconnected event below is
            // the signal, and it already carries no detail by contract.
        }
        finally
        {
            FireDisconnected();
        }
    }

    private void FireDisconnected()
    {
        if (_disconnectedFired)
        {
            return;
        }

        _disconnectedFired = true;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// OpenSSH <c>SHA256:base64</c> fingerprint (no padding) of a raw host
    /// key. This is the format both platforms pin in <see cref="ISshHostKeyTrust"/>.
    /// </summary>
    public static string Fingerprint(byte[] hostKey) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');
}

/// <summary>
/// Builds connected <see cref="ITerminalSession"/> instances for saved SSH
/// hosts. The UI catches <see cref="SshUnknownHostKeyException"/>, asks the
/// user to approve the presented fingerprint, pins it via
/// <see cref="ISshHostKeyTrust"/>, and retries.
/// </summary>
public sealed class SshNetSessionFactory
{
    private readonly ISecretStore _secrets;
    private readonly ISshHostKeyTrust _trust;

    public SshNetSessionFactory(ISecretStore secrets, ISshHostKeyTrust trust)
    {
        _secrets = secrets;
        _trust = trust;
    }

    public async Task<ITerminalSession> ConnectAsync(
        SshHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var session = new SshNetTerminalSession(host, _secrets, _trust);
        await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }
}
