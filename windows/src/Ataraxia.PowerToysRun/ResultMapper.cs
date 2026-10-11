using Hermes.Client.Core;

namespace Ataraxia.PowerToysRun;

/// <summary>
/// A launcher result before it becomes a PowerToys <c>Result</c>.
/// Pure data (no PowerToys types) so mapping unit-tests anywhere.
/// </summary>
internal abstract record PluginResultItem(string Title, string SubTitle);

/// <summary>Deep-links into the Ataraxia app at a session.</summary>
internal sealed record OpenSessionItem(string Title, string SubTitle, string SessionId)
    : PluginResultItem(Title, SubTitle);

/// <summary>Creates a session, sends the message, opens the app there.</summary>
internal sealed record QuickSendItem(string Title, string SubTitle, string Message)
    : PluginResultItem(Title, SubTitle);

/// <summary>Launches the Ataraxia app.</summary>
internal sealed record OpenAppItem(string Title, string SubTitle)
    : PluginResultItem(Title, SubTitle);

/// <summary>
/// Maps gateway data to <see cref="PluginResultItem"/>s. Session search reuses
/// <see cref="SessionSearch"/> from Core (title/preview scoring, OR-ed terms).
/// </summary>
internal static class ResultMapper
{
    private const int MaxItems = 8;

    public static IReadOnlyList<PluginResultItem> MapSessions(
        IReadOnlyList<Session> sessions, string? filter)
    {
        IEnumerable<Session> ordered = sessions;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var hits = SessionSearch.Search(
                sessions,
                new Dictionary<string, IReadOnlyList<ChatMessage>>(),
                filter);
            if (hits.Count == 0)
            {
                return [];
            }

            var scores = hits.ToDictionary(h => h.SessionId, h => h.Score);
            ordered = sessions
                .Where(s => scores.ContainsKey(s.Id))
                .OrderByDescending(s => scores[s.Id]);
        }

        return ordered
            .Where(s => !s.Archived)
            .Take(MaxItems)
            .Select<Session, PluginResultItem>(s => new OpenSessionItem(
                string.IsNullOrWhiteSpace(s.Title) ? $"Session {ShortId(s.Id)}" : s.Title!,
                Describe(s),
                s.Id))
            .ToList();
    }

    public static QuickSendItem MapQuickSend(string message) => new(
        "Ask Ataraxia",
        $"\"{Truncate(message, 60)}\" → new session",
        message);

    public static OpenAppItem MapOpenApp() => new(
        "Open Ataraxia",
        "Launch the Ataraxia app");

    private static string Describe(Session s)
    {
        var count = $"{s.MessageCount} message{(s.MessageCount == 1 ? "" : "s")}";
        var when = s.LastActive?.LocalDateTime.ToString("g");
        return when is null ? count : $"{count} · {when}";
    }

    private static string ShortId(string id) =>
        id.Length <= 8 ? id : id[..8];

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
