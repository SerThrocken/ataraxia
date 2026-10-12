using System;
using Hermes.Client.Core.Terminal;

namespace Hermes.Client.WinUI3.Terminal;

/// <summary>
/// Owns the single interactive PTY session as a singleton: the session
/// survives page navigation (it is not owned by the page) and is torn down
/// — killing the child process tree — when the main window closes.
/// </summary>
public sealed class TerminalSessionHost : IDisposable
{
    private PtySession? _session;
    private bool _disposed;

    public PtySession? Session => _session;

    public event EventHandler? SessionChanged;

    /// <summary>
    /// Returns the live session, resizing it; starts a new one if none is
    /// running (or none ever started).
    /// </summary>
    public PtySession EnsureRunning(string fileName, int cols, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_session is { IsRunning: true } live)
        {
            live.Resize(cols, rows);
            return live;
        }

        return StartNew(fileName, cols, rows);
    }

    /// <summary>Unconditionally starts a fresh session, disposing the old one.</summary>
    public PtySession StartNew(string fileName, int cols, int rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _session?.Dispose();
        var session = new PtySession(new ConPtyBackend());
        session.Start(fileName, cols, rows);
        _session = session;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return session;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Kills the child process tree, closes the pseudoconsole, releases
        // every handle. Called from MainWindow.Closed.
        _session?.Dispose();
        _session = null;
    }
}
