using System;
using System.Text;

namespace Hermes.Client.Core.Terminal;

/// <summary>
/// Owns one interactive terminal session: an <see cref="ITerminalBackend"/>
/// (ConPTY on Windows, a fake in tests) feeding a <see cref="TerminalScreen"/>.
/// Backend callbacks arrive on the reader thread; everything touching the
/// screen is serialized on a private lock. The UI reads the screen through
/// <see cref="UseScreen"/> and is notified via <see cref="OutputProcessed"/>.
/// </summary>
public sealed class PtySession : IDisposable
{
    private readonly ITerminalBackend _backend;
    private readonly TerminalScreen _screen = new();
    private readonly object _gate = new();
    private bool _disposed;

    public PtySession(ITerminalBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _backend.OutputReceived += OnOutputReceived;
        _backend.Exited += OnExited;
    }

    /// <summary>Raised (on the backend reader thread) after output was parsed.</summary>
    public event EventHandler? OutputProcessed;

    /// <summary>Raised at most once when the child exits, with its exit code.</summary>
    public event EventHandler<int>? Exited;

    public bool IsRunning { get; private set; }

    /// <summary>Child exit code, or -1 while running / never started.</summary>
    public int ExitCode { get; private set; } = -1;

    public void Start(string fileName, int cols, int rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRunning)
            {
                throw new InvalidOperationException("Session is already running.");
            }

            _screen.Resize(cols, rows);
            _backend.Start(fileName, "", cols, rows);
            ExitCode = -1;
            IsRunning = true;
        }
    }

    /// <summary>Encodes a key gesture to VT bytes and sends it to the child.</summary>
    public void SendKey(TerminalKeyPress press)
    {
        var bytes = VtKeyEncoder.Encode(press);
        if (bytes.Length > 0)
        {
            _backend.WriteInput(bytes);
        }
    }

    /// <summary>Sends text (typed characters, paste) as UTF-8.</summary>
    public void SendText(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _backend.WriteInput(Encoding.UTF8.GetBytes(text));
        }
    }

    /// <summary>Terminal resize: updates the screen model and the pseudoconsole.</summary>
    public void Resize(int cols, int rows)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _screen.Resize(cols, rows);
        }

        _backend.Resize(cols, rows);
    }

    /// <summary>Runs <paramref name="action"/> against the screen under the session lock.</summary>
    public void UseScreen(Action<TerminalScreen> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            action(_screen);
        }
    }

    private void OnOutputReceived(object? sender, ReadOnlyMemory<byte> data)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _screen.Process(data.Span);
        }

        OutputProcessed?.Invoke(this, EventArgs.Empty);
    }

    private void OnExited(object? sender, int exitCode)
    {
        lock (_gate)
        {
            IsRunning = false;
            ExitCode = exitCode;
        }

        Exited?.Invoke(this, exitCode);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            IsRunning = false;
        }

        // Outside the lock: the backend's reader thread may be inside
        // OnOutputReceived waiting on _gate; Terminate unblocks it.
        _backend.Terminate();
        _backend.OutputReceived -= OnOutputReceived;
        _backend.Exited -= OnExited;
        _backend.Dispose();
    }
}
