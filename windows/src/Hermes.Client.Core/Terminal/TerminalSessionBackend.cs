using System;
using System.Threading.Tasks;

namespace Hermes.Client.Core.Terminal;

/// <summary>
/// Bridges an <see cref="ITerminalSession"/> (Worker 4's SSH sessions, or any
/// future session backend) into the PTY worker's <see cref="ITerminalBackend"/>,
/// so SSH sessions render through the shared <see cref="PtySession"/> +
/// <see cref="TerminalScreen"/> — no second terminal renderer.
///
/// Impedance notes (read before use):
/// <list type="bullet">
/// <item><see cref="ITerminalBackend"/> is synchronous while
/// <see cref="ITerminalSession"/> is async; the blocking calls below are the
/// seam. <see cref="Start"/> dials the network for SSH backends — call it off
/// the UI thread.</item>
/// <item><see cref="Start"/> ignores <paramref name="fileName"/> and
/// <paramref name="arguments"/>: the session already knows its target (the
/// saved SSH host). <paramref name="cols"/>/<paramref name="rows"/> size the
/// remote PTY.</item>
/// <item><see cref="ITerminalBackend.Exited"/> fires with <c>-1</c> (unknown):
/// an SSH shell channel close has no process exit code. <see cref="PtySession"/>
/// uses <c>-1</c> for "unknown" already, so this is consistent.</item>
/// </list>
/// </summary>
public sealed class TerminalSessionBackend : ITerminalBackend
{
    private readonly ITerminalSession _session;
    private bool _disposed;

    public TerminalSessionBackend(ITerminalSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.DataReceived += OnDataReceived;
        _session.Disconnected += OnDisconnected;
    }

    /// <summary>
    /// Raw output bytes from the session. Raised on the session's pump thread —
    /// <see cref="PtySession"/> serializes them.
    /// </summary>
    public event EventHandler<ReadOnlyMemory<byte>>? OutputReceived;

    /// <summary>
    /// The session ended. Raised at most once, always with <c>-1</c>
    /// (no exit code for SSH shell channels).
    /// </summary>
    public event EventHandler<int>? Exited;

    public void Start(string fileName, string arguments, int cols, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session.ConnectAsync().GetAwaiter().GetResult();
        _session.ResizeAsync(cols, rows).GetAwaiter().GetResult();
    }

    public void WriteInput(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session.WriteAsync(data.ToArray()).GetAwaiter().GetResult();
    }

    public void Resize(int cols, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session.ResizeAsync(cols, rows).GetAwaiter().GetResult();
    }

    public void Terminate()
    {
        if (_disposed)
        {
            return;
        }

        // ITerminalSession.CloseAsync is idempotent by contract.
        _session.CloseAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.DataReceived -= OnDataReceived;
        _session.Disconnected -= OnDisconnected;
        _session.DisposeAsync().GetAwaiter().GetResult();
    }

    private void OnDataReceived(object? sender, TerminalDataEventArgs e) =>
        OutputReceived?.Invoke(this, e.Data);

    private void OnDisconnected(object? sender, EventArgs e) =>
        Exited?.Invoke(this, -1);
}
