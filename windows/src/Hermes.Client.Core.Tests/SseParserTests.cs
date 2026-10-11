using Xunit;
using System.Text;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class SseParserTests
{
    private static async Task<List<SseEvent>> ReadAllAsync(string sse)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse));
        var events = new List<SseEvent>();
        await foreach (var e in SseParser.ReadEventsAsync(stream))
        {
            events.Add(e);
        }
        return events;
    }

    [Fact]
    public async Task ParsesSingleFrameWithEnvelope()
    {
        const string sse = "event: assistant.delta\n" +
            "data: {\"message_id\":\"m1\",\"delta\":\"ok\",\"session_id\":\"s1\",\"run_id\":\"r1\",\"seq\":3,\"ts\":1791734804.7}\n\n";

        var events = await ReadAllAsync(sse);

        Assert.Single(events);
        var e = events[0];
        Assert.Equal("assistant.delta", e.Name);
        Assert.Equal("s1", e.SessionId);
        Assert.Equal("r1", e.RunId);
        Assert.Equal(3, e.Seq);
        Assert.Equal("m1", e.GetString("message_id"));
        Assert.Equal("ok", e.GetString("delta"));
        foreach (var ev in events) ev.Dispose();
    }

    [Fact]
    public async Task SkipsKeepaliveComments()
    {
        const string sse = ": keepalive\n\n" +
            "event: done\n" +
            "data: {\"session_id\":\"s1\",\"run_id\":\"r1\",\"seq\":7,\"ts\":1791734811.6}\n\n" +
            ": keepalive\n\n";

        var events = await ReadAllAsync(sse);

        Assert.Single(events);
        Assert.Equal("done", events[0].Name);
        foreach (var ev in events) ev.Dispose();
    }

    [Fact]
    public async Task JoinsMultiLineDataWithNewline()
    {
        const string sse = "event: custom\n" +
            "data: {\"a\": 1,\n" +
            "data: \"b\": 2}\n\n";

        var events = await ReadAllAsync(sse);

        Assert.Single(events);
        Assert.Equal(1, events[0].Data.GetProperty("a").GetInt32());
        Assert.Equal(2, events[0].Data.GetProperty("b").GetInt32());
        foreach (var ev in events) ev.Dispose();
    }

    [Fact]
    public async Task DispatchesTrailingFrameWithoutBlankLine()
    {
        const string sse = "event: run.completed\n" +
            "data: {\"session_id\":\"s1\",\"run_id\":\"r1\",\"seq\":6,\"ts\":1.5}";

        var events = await ReadAllAsync(sse);

        Assert.Single(events);
        Assert.Equal("run.completed", events[0].Name);
        foreach (var ev in events) ev.Dispose();
    }

    [Fact]
    public async Task MissingEnvelopeFieldsDefaultGracefully()
    {
        const string sse = "event: error\n" +
            "data: {\"message\":\"boom\"}\n\n";

        var events = await ReadAllAsync(sse);

        Assert.Single(events);
        Assert.Null(events[0].SessionId);
        Assert.Null(events[0].RunId);
        Assert.Equal(0, events[0].Seq);
        Assert.Equal("boom", events[0].GetString("message"));
        foreach (var ev in events) ev.Dispose();
    }

    [Fact]
    public async Task MalformedDataFrameIsSkippedNotFatal()
    {
        const string sse = "event: assistant.delta\n" +
            "data: not-json{{{\n\n" +
            "event: done\n" +
            "data: {}\n\n";

        var events = await ReadAllAsync(sse);

        Assert.Single(events);
        Assert.Equal("done", events[0].Name);
        foreach (var ev in events) ev.Dispose();
    }
}
