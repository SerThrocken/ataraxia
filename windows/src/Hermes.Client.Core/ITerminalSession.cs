using System.Text;

namespace Hermes.Client.Core;

/// <summary>
/// Bytes flowing from the remote end of a terminal session.
/// The payload is raw terminal output (VT/ANSI escape sequences included) —
/// rendering is the view's job; backends never render.
/// </summary>
public sealed class TerminalDataEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
}

/// <summary>
/// Shared terminal-session contract between session <em>backends</em> and
/// terminal <em>views</em>. This is the seam the SSH worker (backends) and the
/// PTY worker (views) coordinate through — see TERMINAL-SEAM.md at the repo
/// root for the full contract, threading rules, and the Android mirror
/// (<c>com.tlg3d.hermes.ssh.TerminalSession</c>).
///
/// Contract summary:
/// <list type="bullet">
/// <item>One instance = one live session (one SSH shell channel, or one
/// local PTY). It is not reusable after <see cref="CloseAsync"/>.</item>
/// <item><see cref="DataReceived"/> carries raw terminal bytes from the
/// remote end. The view feeds them verbatim into its terminal emulator;
/// the backend never interprets escape sequences.</item>
/// <item><see cref="WriteAsync(byte[], CancellationToken)"/> carries
/// keystrokes / pasted input toward the remote end, verbatim.</item>
/// <item><see cref="ResizeAsync"/> notifies the remote PTY of the view's
/// new size in character cells. Views call it on layout changes.</item>
/// <item><see cref="Disconnected"/> fires exactly once when the session ends
/// for any reason (remote close, network drop, <see cref="CloseAsync"/>).
/// After it fires, the instance is done — the view drops its reference.</item>
/// <item>Backends own all transport threads and pumps; the view only ever
/// calls the async methods and subscribes to the events.</item>
/// </list>
/// </summary>
public interface ITerminalSession : IAsyncDisposable
{
    /// <summary>Display title (host name for SSH, shell name for local PTY).</summary>
    string Title { get; }

    /// <summary>Whether the transport is currently up.</summary>
    bool IsConnected { get; }

    /// <summary>Raw terminal output from the remote end. See contract above.</summary>
    event EventHandler<TerminalDataEventArgs>? DataReceived;

    /// <summary>
    /// Fires exactly once when the session ends. The view must unsubscribe
    /// and drop the instance afterwards.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// Establishes the session. May throw
    /// <see cref="SshUnknownHostKeyException"/> (SSH backends) when the
    /// server's host key is not pinned — the UI prompts and retries.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends raw input bytes (keystrokes, paste) to the remote end.</summary>
    Task WriteAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>UTF-8 convenience overload for <see cref="WriteAsync(byte[], CancellationToken)"/>.</summary>
    Task WriteAsync(string text, CancellationToken cancellationToken = default) =>
        WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);

    /// <summary>Notifies the remote PTY of the view's size in character cells.</summary>
    Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the session: stops the pump, closes the transport, fires
    /// <see cref="Disconnected"/>. Idempotent.
    /// </summary>
    Task CloseAsync();
}
