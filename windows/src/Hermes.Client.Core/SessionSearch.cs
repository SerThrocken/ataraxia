namespace Hermes.Client.Core;

/// <summary>
/// One search hit: either a session-title/preview hit (<see cref="MessageId"/>
/// null, <see cref="Role"/> "session") or a message-content hit.
/// </summary>
public sealed record SearchHit(
    string SessionId,
    string? SessionTitle,
    string? MessageId,
    string Role,
    string Snippet,
    double Score);

/// <summary>
/// Full-text search over cached sessions and their messages. Pure and
/// synchronous: no I/O, so the UI searches whatever it has already fetched.
/// Terms are OR-ed (a hit needs at least one term); title matches outrank
/// message matches; ties keep session order.
/// </summary>
public static class SessionSearch
{
    private const int TitleWeight = 3;
    private const int PreviewWeight = 1;
    private const int MessageWeight = 1;
    private const int SnippetRadius = 60;

    public static IReadOnlyList<SearchHit> Search(
        IReadOnlyList<Session> sessions,
        IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>> messagesBySession,
        string query)
    {
        var terms = Tokenize(query);
        if (terms.Count == 0 || sessions.Count == 0)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        foreach (var session in sessions)
        {
            if (ScoreSession(session, messagesBySession, terms) is { } hit)
            {
                hits.Add(hit);
            }
        }

        // OrderByDescending is stable: equal scores keep session order.
        return hits.OrderByDescending(h => h.Score).ToList();
    }

    private static SearchHit? ScoreSession(
        Session session,
        IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>> messagesBySession,
        IReadOnlyList<string> terms)
    {
        var titleScore = ScoreText(session.Title, terms, TitleWeight);
        var previewScore = ScoreText(session.Preview, terms, PreviewWeight);

        ChatMessage? bestMessage = null;
        var bestMessageScore = 0;
        string? bestTerm = null;
        if (messagesBySession.TryGetValue(session.Id, out var messages))
        {
            foreach (var message in messages)
            {
                var (score, term) = ScoreMessage(message.Content, terms);
                if (score > bestMessageScore)
                {
                    bestMessageScore = score;
                    bestMessage = message;
                    bestTerm = term;
                }
            }
        }

        var total = titleScore + previewScore + bestMessageScore;
        if (total == 0)
        {
            return null;
        }

        if (bestMessage is not null && bestMessageScore >= titleScore + previewScore)
        {
            return new SearchHit(
                session.Id,
                session.Title,
                bestMessage.Id,
                bestMessage.Role,
                BuildSnippet(bestMessage.Content, bestTerm ?? terms[0]),
                total);
        }

        return new SearchHit(
            session.Id,
            session.Title,
            null,
            "session",
            session.Title ?? session.Preview ?? session.Id,
            total);
    }

    private static int ScoreText(string? text, IReadOnlyList<string> terms, int weight)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var score = 0;
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += weight;
            }
        }

        return score;
    }

    private static (int Score, string? Term) ScoreMessage(string content, IReadOnlyList<string> terms)
    {
        var score = 0;
        string? firstTerm = null;
        foreach (var term in terms)
        {
            if (content.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += MessageWeight;
                firstTerm ??= term;
            }
        }

        return (score, firstTerm);
    }

    private static string BuildSnippet(string content, string term)
    {
        var index = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return content.Length <= SnippetRadius * 2
                ? content
                : content[..(SnippetRadius * 2)] + "…";
        }

        var start = Math.Max(0, index - SnippetRadius);
        var end = Math.Min(content.Length, index + term.Length + SnippetRadius);
        var snippet = content[start..end].Trim();
        if (start > 0)
        {
            snippet = "…" + snippet;
        }

        if (end < content.Length)
        {
            snippet += "…";
        }

        return snippet;
    }

    private static IReadOnlyList<string> Tokenize(string query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .Distinct()
                .ToList();
}
