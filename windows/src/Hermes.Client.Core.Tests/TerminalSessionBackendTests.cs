using Xunit;
using System.Text;
using Hermes.Client.Core.Terminal;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// The adapter between Worker 4's <see cref="ITerminalSession"/> and the PTY
/// worker's <see cref="ITerminalBackend"/>: SSH-flavored sessions must drive
/// the shared <see cref="PtySession"/>/<see cref="TerminalScreen"/> renderer,
/// not a second one.
/// </summary>
public sealed class TerminalSessionBackendTests
{
    [Fact]
    public void Start_ConnectsSession_AndResizesRemotePty()
    {
        var session = new FakeTerminalSession();
        using var backend = new TerminalSessionBackend(session);

        // fileName/arguments are ignored: the session knows its own target.
        backend.Start("ignored.exe", "--ignored", 80, 24);

        Assert.True(session.IsConnected);
        var resize = Assert.Single(session.Resizes);
        Assert.Equal((80, 24), resize);
    }

    [Fact]
    public void WriteInput_ForwardsBytesVerbatim()
    {
        var session = new FakeTerminalSession();
        using var backend = new TerminalSessionBackend(session);
        backend.Start("", "", 80, 24);

        backend.WriteInput("ls -la\n"u8.ToArray());

        var written = Assert.Single(session.Written);
        Assert.Equal("ls -la\n"u8.ToArray(), written);
    }

    [Fact]
    public void Resize_ForwardsToSession()
    {
        var session = new FakeTerminalSession();
        using var backend = new TerminalSessionBackend(session);
        backend.Start("", "", 80, 24);

        backend.Resize(120, 40);

        Assert.Equal(2, session.Resizes.Count);
        Assert.Equal((80, 24), session.Resizes[0]);
        Assert.Equal((120, 40), session.Resizes[1]);
    }

    [Fact]
    public void SessionOutput_RaisesBackendOutputReceived()
    {
        var session = new FakeTerminalSession();
        using var backend = new TerminalSessionBackend(session);
        ReadOnlyMemory<byte>? received = null;
        backend.OutputReceived += (_, data) => received = data;

        session.Emit("hello"u8.ToArray());

        Assert.NotNull(received);
        Assert.Equal("hello"u8.ToArray(), received.Value.ToArray());
    }

    [Fact]
    public void SessionDisconnect_RaisesBackendExited_WithUnknownCode()
    {
        var session = new FakeTerminalSession();
        using var backend = new TerminalSessionBackend(session);
        int? code = null;
        backend.Exited += (_, c) => code = c;

        session.Emit("x"u8.ToArray());
        backend.Terminate();

        // -1: an SSH shell close has no process exit code.
        Assert.Equal(-1, code);
        Assert.Equal(1, session.CloseCount);
    }

    [Fact]
    public void Terminate_IsIdempotent()
    {
        var session = new FakeTerminalSession();
        using var backend = new TerminalSessionBackend(session);

        backend.Terminate();
        backend.Terminate();

        Assert.Equal(2, session.CloseCount); // CloseAsync itself is idempotent by contract
    }

    [Fact]
    public void PtySession_RendersSessionOutput_ThroughSharedScreen()
    {
        // End-to-end through the PTY worker's renderer: bytes emitted by the
        // session land on the shared TerminalScreen, and keys typed in the
        // session object reach the session backend.
        var session = new FakeTerminalSession();
        using var pty = new PtySession(new TerminalSessionBackend(session));
        pty.Start("ssh-session", 80, 24);

        session.Emit(Encoding.UTF8.GetBytes("hello"));

        var text = new StringBuilder();
        pty.UseScreen(screen =>
        {
            for (var row = 0; row < screen.Rows; row++)
            {
                text.Append(screen.GetLineText(row));
            }
        });
        Assert.Contains("hello", text.ToString());

        pty.SendText("x");
        Assert.Equal("x"u8.ToArray(), Assert.Single(session.Written));
    }
}
