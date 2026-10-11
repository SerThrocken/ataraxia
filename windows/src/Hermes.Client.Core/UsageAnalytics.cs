namespace Hermes.Client.Core;

/// <summary>One UTC day's spend, rolled up from session totals.</summary>
public sealed record DailySpend(DateOnly Date, long InputTokens, long OutputTokens, double CostUsd)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>Spend for one provider (the model id's prefix before "/").</summary>
public sealed record ProviderSpend(
    string Provider, long InputTokens, long OutputTokens, double CostUsd, int SessionCount)
{
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>One session's spend, ranked by cost for the leaderboard.</summary>
public sealed record SessionSpend(
    string SessionId, string? Title, string? Model, long TotalTokens, double? CostUsd,
    DateTimeOffset? LastActive)
{
    /// <summary>Leaderboard line, mirroring <see cref="TurnUsage.Summary"/>.</summary>
    public string Summary =>
        $"{(string.IsNullOrWhiteSpace(Title) ? SessionId : Title)} · " +
        (CostUsd is { } cost ? $"${cost:F4}" : "no cost reported") +
        $" · {TotalTokens:N0} tokens";
}

public sealed record AnalyticsSummary(
    int SessionCount,
    long TotalTokens,
    double TotalCostUsd,
    IReadOnlyList<DailySpend> PerDay,
    IReadOnlyList<ProviderSpend> PerProvider,
    IReadOnlyList<SessionSpend> TopSessions);

/// <summary>
/// Cost analytics over the gateway's authoritative session list. The 8642
/// surface exposes no server-side usage aggregation (verified: /api/usage
/// and /v1/usage both 404), so per-day / per-provider / per-session spend
/// is rolled up client-side from each session's stored token totals and
/// <c>estimated_cost_usd</c>. This extends the session-scoped
/// <see cref="UsageMeter"/> data; no local rate table is ever used.
/// </summary>
public static class UsageAnalytics
{
    /// <summary>
    /// Sessions with no timestamp group under <see cref="DateOnly.MinValue"/>
    /// ("date unknown"); a null model id groups under "unknown".
    /// </summary>
    public static AnalyticsSummary Summarize(IEnumerable<Session> sessions, int topSessions = 10)
    {
        var list = sessions.ToList();

        var perDay = list
            .GroupBy(s => s.LastActive is { } t
                ? DateOnly.FromDateTime(t.UtcDateTime)
                : DateOnly.MinValue)
            .OrderBy(g => g.Key)
            .Select(g => new DailySpend(
                g.Key,
                g.Sum(s => s.InputTokens),
                g.Sum(s => s.OutputTokens),
                g.Sum(s => s.EstimatedCostUsd ?? 0)))
            .ToList();

        var perProvider = list
            .GroupBy(s => ProviderOf(s.Model))
            .OrderByDescending(g => g.Sum(s => s.EstimatedCostUsd ?? 0))
            .Select(g => new ProviderSpend(
                g.Key,
                g.Sum(s => s.InputTokens),
                g.Sum(s => s.OutputTokens),
                g.Sum(s => s.EstimatedCostUsd ?? 0),
                g.Count()))
            .ToList();

        var top = list
            .OrderByDescending(s => s.EstimatedCostUsd ?? 0)
            .Take(topSessions)
            .Select(s => new SessionSpend(
                s.Id, s.Title, s.Model,
                s.InputTokens + s.OutputTokens,
                s.EstimatedCostUsd, s.LastActive))
            .ToList();

        return new AnalyticsSummary(
            list.Count,
            list.Sum(s => s.InputTokens + s.OutputTokens),
            list.Sum(s => s.EstimatedCostUsd ?? 0),
            perDay,
            perProvider,
            top);
    }

    /// <summary>"openai/gpt-5.6-luna-pro" → "openai"; null/empty → "unknown".</summary>
    public static string ProviderOf(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "unknown";
        }

        var slash = model.IndexOf('/');
        return slash > 0 ? model[..slash] : model;
    }
}
