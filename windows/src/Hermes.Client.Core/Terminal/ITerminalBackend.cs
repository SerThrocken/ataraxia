using System;

namespace Hermes.Client.Core.Terminal;

/// <summary>
/// Platform PTY backend. The WinUI app implements this with ConPTY
/// (<c>ConPtyBackend</c>); tests substitute a fake. All callbacks fire on
/// the backend's reader thread — <see cref="PtySession"/> serializes them.
/// </summary>
public interface ITerminalBackend : IDisposable
{
    /// <summary>
    /// Raw output bytes from the child. Raised on the reader thread.
    /// The memory is only valid for the duration of the event.
    /// </summary>
    event EventHandler<ReadOnlyMemory<byte>>? OutputReceived;

    /// <summary>
    /// The child process exited. Raised at most once, with its exit code.
    /// </summary>
    event EventHandler<int>? Exited;

    /// <summary>
    /// Spawns the child attached to a new pseudoconsole of the given size.
    /// </summary>
    void Start(string fileName, string arguments, int cols, int rows);

    /// <summary>Writes raw bytes to the child's stdin.</summary>
    void WriteInput(ReadOnlySpan<byte> data);

    /// <summary>Resizes the pseudoconsole (terminal resize).</summary>
    void Resize(int cols, int rows);

    /// <summary>
    /// Kills the child process tree. Idempotent; safe to call before
    /// <see cref="IDisposable.Dispose"/>.
    /// </summary>
    void Terminate();
}
