namespace Hermes.Client.Core;

/// <summary>
/// Command-palette ranking: case-insensitive substring matching with a simple
/// additive score. Prefix beats word-boundary beats early-position beats
/// short-target. No external library (ponytail: stdlib only).
/// </summary>
public static class PaletteMatcher
{
    /// <summary>
    /// Scores <paramref name="query"/> against <paramref name="target"/>.
    /// Higher is better; <c>null</c> means no match. An empty query matches
    /// everything with a neutral score (the caller keeps curated order).
    /// </summary>
    public static int? Score(string? query, string? target)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return null;
        }

        var q = query.Trim();
        var index = target.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var score = 1000 - (index * 10) - target.Length;
        if (index == 0)
        {
            score += 200; // prefix match
        }
        else if (!char.IsLetterOrDigit(target[index - 1]))
        {
            score += 100; // word-boundary match
        }

        return score;
    }
}
