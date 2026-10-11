using Hermes.Client.Core;
using Xunit;

namespace Ataraxia.PowerToysRun.Tests;

public sealed class ResultMapperTests
{
    private static Session MakeSession(
        string id, string? title, bool archived = false, int messages = 3) =>
        new(
            Id: id,
            Title: title,
            Preview: "preview",
            MessageCount: messages,
            Model: "test-model",
            LastActive: new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero),
            StartedAt: null,
            EndedAt: null,
            Pinned: false,
            Archived: archived);

    private static IReadOnlyList<Session> TwoSessions() =>
    [
        MakeSession("aaa111", "Deploy notes"),
        MakeSession("bbb222", "Grocery list"),
    ];

    [Fact]
    public void MapSessions_without_filter_returns_all_unarchived()
    {
        var items = ResultMapper.MapSessions(TwoSessions(), filter: null);

        Assert.Equal(2, items.Count);
        var first = Assert.IsType<OpenSessionItem>(items[0]);
        Assert.Equal("Deploy notes", first.Title);
        Assert.Equal("aaa111", first.SessionId);
    }

    [Fact]
    public void MapSessions_excludes_archived()
    {
        var sessions = new List<Session>(TwoSessions()) { MakeSession("ccc333", "Old", archived: true) };

        var items = ResultMapper.MapSessions(sessions, filter: null);

        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, i => i is OpenSessionItem s && s.SessionId == "ccc333");
    }

    [Fact]
    public void MapSessions_with_filter_matches_title()
    {
        var items = ResultMapper.MapSessions(TwoSessions(), "deploy");

        var single = Assert.Single(items);
        var session = Assert.IsType<OpenSessionItem>(single);
        Assert.Equal("aaa111", session.SessionId);
    }

    [Fact]
    public void MapSessions_with_filter_no_match_returns_empty()
    {
        var items = ResultMapper.MapSessions(TwoSessions(), "zzz-no-such-session");

        Assert.Empty(items);
    }

    [Fact]
    public void MapSessions_untitled_session_gets_short_id_title()
    {
        var items = ResultMapper.MapSessions([MakeSession("abcdef123456", null)], filter: null);

        var single = Assert.Single(items);
        Assert.Equal("Session abcdef12", single.Title);
    }

    [Fact]
    public void MapQuickSend_carries_message()
    {
        var item = ResultMapper.MapQuickSend("hello there");

        Assert.Equal("Ask Ataraxia", item.Title);
        Assert.Equal("hello there", item.Message);
        Assert.Contains("hello there", item.SubTitle);
    }

    [Fact]
    public void MapQuickSend_truncates_long_message_in_subtitle()
    {
        var longMessage = new string('x', 100);

        var item = ResultMapper.MapQuickSend(longMessage);

        Assert.Equal(longMessage, item.Message);
        Assert.Contains("…", item.SubTitle);
    }

    [Fact]
    public void MapOpenApp_has_title()
    {
        var item = ResultMapper.MapOpenApp();

        Assert.Equal("Open Ataraxia", item.Title);
    }
}
