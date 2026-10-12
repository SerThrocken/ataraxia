using System;
using System.Collections.Generic;
using System.Text;
using Hermes.Client.Core.Terminal;
using Xunit;

namespace Hermes.Client.Core.Tests.Terminal;

/// <summary>
/// Session lifecycle (<c>PtySession</c>) against a fake <see cref="ITerminalBackend"/>:
/// input encoding round-trip, output parsing, resize forwarding, exit
/// surfacing, and disposal killing the backend.
/// </summary>
public sealed class PtySessionTests
{
    private sealed class FakeBackend : ITerminalBackend
    {
        public event EventHandler<ReadOnlyMemory<byte>>? OutputReceived;
        public event EventHandler<int>? Exited;

        public readonly List<byte[]> Written = new();
        public readonly List<(int Cols, int Rows)> Resizes = new();
        public bool Started;
        public bool Terminated;
        public bool Disposed;

        public void Start(string fileName, string arguments, int cols, int rows) => Started = true;

        public void WriteInput(ReadOnlySpan<byte> data) => Written.Add(data.ToArray());

        public void Resize(int cols, int rows) => Resizes.Add((cols, rows));

        public void Terminate() => Terminated = true;

        public void Dispose() => Disposed = true;

        public void PushOutput(string text) =>
            OutputReceived?.Invoke(this, Encoding.UTF8.GetBytes(text));

        public void PushExit(int code) => Exited?.Invoke(this, code);
    }

    [Fact]
    public void Start_MarksRunning_AndStartsBackend()
    {
        using var session = new PtySession(new FakeBackend());
        Assert.False(session.IsRunning);
        session.Start("shell.exe", 80, 24);
        Assert.True(session.IsRunning);
        Assert.Equal(-1, session.ExitCode);
    }

    [Fact]
    public void Start_Twice_Throws()
    {
        var backend = new FakeBackend();
        using var session = new PtySession(backend);
        session.Start("shell.exe", 80, 24);
        Assert.Throws<InvalidOperationException>(() => session.Start("shell.exe", 80, 24));
    }

    [Fact]
    public void SendKey_EncodesCtrlC_AsEtx()
    {
        var backend = new FakeBackend();
        using var session = new PtySession(backend);
        session.Start("shell.exe", 80, 24);
        session.SendKey(new(TerminalKey.Character, TerminalModifiers.Control, 'c'));
        Assert.Single(backend.Written);
        Assert.Equal([0x03], backend.Written[0]);
    }

    [Fact]
    public void SendKey_ArrowUp_SendsCsiSequence()
    {
        var backend = new FakeBackend();
        using var session = new PtySession(backend);
        session.Start("shell.exe", 80, 24);
        session.SendKey(new(TerminalKey.ArrowUp));
        Assert.Equal("\x1b[A", Encoding.ASCII.GetString(backend.Written[0]));
    }

    [Fact]
    public void SendText_SendsUtf8Bytes()
    {
        var backend = new FakeBackend();
        using var session = new PtySession(backend);
        session.Start("shell.exe", 80, 24);
        session.SendText("héllo");
        Assert.Equal("héllo", Encoding.UTF8.GetString(backend.Written[0]));
    }

    [Fact]
    public void Output_IsParsedIntoScreen_AndNotifies()
    {
        var backend = new FakeBackend();
        using var session = new PtySession(backend);
        session.Start("shell.exe", 80, 24);

        var notified = 0;
        session.OutputProcessed += (_, _) => notified++;

        backend.PushOutput("hello\r\n");
        Assert.Equal(1, notified);

        string? line = null;
        session.UseScreen(s => line = s.GetLineText(0));
        Assert.Equal("hello", line);
    }

    [Fact]
    public void Resize_UpdatesScreen_AndForwardsToBackend()
    {
        var backend = new FakeBackend();
        using var session = new PtySession(backend);
        session.Start("shell.exe", 80, 24);
        session.Resize(120, 40);
        Assert.Contains((120, 40), backend.Resizes);

        int cols = 0, rows = 0;
        session.UseScreen(s =>
        {
            cols = s.Cols;
            rows = s.Rows;
        });
        Assert.Equal(120, cols);
        Assert.Equal(40, rows);
    }

    [Fact]
    public void Exit_SurfacesExitCode_AndStopsRunning()
    {
        var backend = new FakeBackend();
        using var session = new PtySession(backend);
        session.Start("shell.exe", 80, 24);

        int? code = null;
        session.Exited += (_, c) => code = c;
        backend.PushExit(3);

        Assert.Equal(3, code);
        Assert.Equal(3, session.ExitCode);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public void Dispose_Terminates_AndDisposesBackend()
    {
        var backend = new FakeBackend();
        var session = new PtySession(backend);
        session.Start("shell.exe", 80, 24);
        session.Dispose();
        Assert.True(backend.Terminated);
        Assert.True(backend.Disposed);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var backend = new FakeBackend();
        var session = new PtySession(backend);
        session.Dispose();
        session.Dispose(); // must not throw
        Assert.True(backend.Disposed);
    }
}
