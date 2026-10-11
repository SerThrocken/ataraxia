using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class SessionSearchTests
{
    private static Session MakeSession(string id, string? title, string? preview = null) =>
        new(id, title, preview, 3, "m", DateTimeOffset.UtcNow, null, null, false, false);

    private static ChatMessage MakeMessage(string id, string sessionId, string role, string content) =>
        new(id, sessionId, role, content, DateTimeOffset.UtcNow, null, null, null, null, null);

    private static IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>> Messages(
        params (string sessionId, ChatMessage message)[] pairs)
    {
        var dict = new Dictionary<string, List<ChatMessage>>();
        foreach (var (sessionId, message) in pairs)
        {
            if (!dict.TryGetValue(sessionId, out var list))
            {
                dict[sessionId] = list = new List<ChatMessage>();
            }
            list.Add(message);
        }
        return dict.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<ChatMessage>)kv.Value);
    }

    [Fact]
    public void EmptyQueryReturnsNoHits()
    {
        var sessions = new[] { MakeSession("s1", "Hello") };
        Assert.Empty(SessionSearch.Search(sessions, Messages(), ""));
        Assert.Empty(SessionSearch.Search(sessions, Messages(), "   "));
    }

    [Fact]
    public void TitleMatchOutranksContentOnlyMatch()
    {
        var sessions = new[]
        {
            MakeSession("s1", "unrelated title"),
            MakeSession("s2", "restart diagnostics"),
        };
        var messages = Messages(
            ("s1", MakeMessage("m1", "s1", "user", "please restart the printer")));

        var hits = SessionSearch.Search(sessions, messages, "restart");

        Assert.Equal(2, hits.Count);
        Assert.Equal("s2", hits[0].SessionId); // title hit first
        Assert.Equal("s1", hits[1].SessionId);
        Assert.Equal("m1", hits[1].MessageId);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var sessions = new[] { MakeSession("s1", "RESTART Bugs") };
        var hits = SessionSearch.Search(sessions, Messages(), "restart");
        Assert.Single(hits);
    }

    [Fact]
    public void MultiTermScoresHigher()
    {
        var sessions = new[]
        {
            MakeSession("s1", "printer restart"),
            MakeSession("s2", "printer jam"),
        };
        var hits = SessionSearch.Search(sessions, Messages(), "printer restart");
        Assert.Equal(2, hits.Count);
        Assert.Equal("s1", hits[0].SessionId);
        Assert.True(hits[0].Score > hits[1].Score);
    }

    [Fact]
    public void SnippetCentersOnFirstMatchAndIsBounded()
    {
        var body = new string('x', 200) + " needle " + new string('y', 200);
        var sessions = new[] { MakeSession("s1", "t") };
        var messages = Messages(("s1", MakeMessage("m1", "s1", "assistant", body)));

        var hit = Assert.Single(SessionSearch.Search(sessions, messages, "needle"));

        Assert.Contains("needle", hit.Snippet);
        Assert.True(hit.Snippet.Length <= 140, $"snippet too long: {hit.Snippet.Length}");
        Assert.Equal("assistant", hit.Role);
    }

    [Fact]
    public void NoMatchReturnsEmpty()
    {
        var sessions = new[] { MakeSession("s1", "printer") };
        var messages = Messages(("s1", MakeMessage("m1", "s1", "user", "fix the jam")));
        Assert.Empty(SessionSearch.Search(sessions, messages, "zebra"));
    }

    [Fact]
    public void NullTitleDoesNotThrow()
    {
        var sessions = new[] { MakeSession("s1", null) };
        var messages = Messages(("s1", MakeMessage("m1", "s1", "user", "restart now")));
        var hits = SessionSearch.Search(sessions, messages, "restart");
        Assert.Single(hits);
    }

    [Fact]
    public void SessionWithoutCachedMessagesStillSearchesTitle()
    {
        var sessions = new[] { MakeSession("s1", "restart plan") };
        var hits = SessionSearch.Search(sessions, Messages(), "restart");
        var hit = Assert.Single(hits);
        Assert.Null(hit.MessageId);
        Assert.Equal("session", hit.Role);
    }
}
