using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Pure-aggregation tests for <see cref="UsageAnalytics"/>: per-day,
/// per-provider, and per-session spend rolled up from the gateway's
/// authoritative session list. No gateway-side usage endpoint exists on
/// 8642, so the session list (tokens + <c>estimated_cost_usd</c>) is the
/// source of truth.
/// </summary>
public sealed class UsageAnalyticsTests
{
    private static Session S(
        string id, string? model, long inTok, long outTok, double? cost, DateTimeOffset? lastActive) =>
        new(id, "title " + id, null, 1, model, lastActive, lastActive, null, false, false, inTok, outTok, cost);

    private static readonly DateTimeOffset Day1 = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SummarizeTotalsTokensAndCost()
    {
        var sessions = new[]
        {
            S("a", "openai/gpt-5", 100, 50, 0.01, Day1),
            S("b", "xkiro/claude", 200, 100, 0.03, Day1),
        };

        var summary = UsageAnalytics.Summarize(sessions);

        Assert.Equal(450L, summary.TotalTokens);
        Assert.Equal(0.04, summary.TotalCostUsd, precision: 6);
        Assert.Equal(2, summary.SessionCount);
    }

    [Fact]
    public void SummarizeGroupsByUtcDay()
    {
        var sessions = new[]
        {
            S("a", "openai/gpt-5", 100, 50, 0.01, Day1),
            S("b", "openai/gpt-5", 200, 100, 0.02, Day2),
        };

        var summary = UsageAnalytics.Summarize(sessions);

        Assert.Equal(2, summary.PerDay.Count);
        var d1 = summary.PerDay.Single(d => d.Date == DateOnly.FromDateTime(Day1.UtcDateTime));
        Assert.Equal(150L, d1.TotalTokens);
        Assert.Equal(0.01, d1.CostUsd, precision: 6);
        Assert.Equal(
            new[] { DateOnly.FromDateTime(Day1.UtcDateTime), DateOnly.FromDateTime(Day2.UtcDateTime) },
            summary.PerDay.Select(d => d.Date).OrderBy(d => d).ToArray());
    }

    [Fact]
    public void SummarizeGroupsByProviderFromModelPrefix()
    {
        var sessions = new[]
        {
            S("a", "openai/gpt-5", 100, 50, 0.01, Day1),
            S("b", "openai/gpt-4", 100, 50, 0.02, Day1),
            S("c", "xkiro/claude", 100, 50, 0.05, Day1),
            S("d", null, 100, 50, null, Day1),
        };

        var summary = UsageAnalytics.Summarize(sessions);

        Assert.Equal(3, summary.PerProvider.Count);
        var openai = summary.PerProvider.Single(p => p.Provider == "openai");
        Assert.Equal(300L, openai.TotalTokens);
        Assert.Equal(0.03, openai.CostUsd, precision: 6);
        Assert.Equal(2, openai.SessionCount);
        var unknown = summary.PerProvider.Single(p => p.Provider == "unknown");
        Assert.Equal(0.0, unknown.CostUsd, precision: 6);
    }

    [Fact]
    public void SummarizeRanksSessionsByCost()
    {
        var sessions = new[]
        {
            S("cheap", "openai/gpt-5", 10, 10, 0.001, Day1),
            S("pricey", "openai/gpt-5", 10, 10, 0.05, Day1),
            S("free", "openai/gpt-5", 10, 10, null, Day1),
        };

        var summary = UsageAnalytics.Summarize(sessions, topSessions: 2);

        Assert.Equal(new[] { "pricey", "cheap" }, summary.TopSessions.Select(s => s.SessionId).ToArray());
    }

    [Fact]
    public void SummarizeEmptyYieldsZeroes()
    {
        var summary = UsageAnalytics.Summarize([]);

        Assert.Equal(0, summary.SessionCount);
        Assert.Equal(0.0, summary.TotalCostUsd);
        Assert.Empty(summary.PerDay);
        Assert.Empty(summary.PerProvider);
        Assert.Empty(summary.TopSessions);
    }

    [Fact]
    public void SessionsWithoutDateLandOnUnknownDay()
    {
        var sessions = new[] { S("a", "openai/gpt-5", 10, 10, 0.01, null) };

        var summary = UsageAnalytics.Summarize(sessions);

        Assert.Single(summary.PerDay);
        Assert.Equal(DateOnly.MinValue, summary.PerDay[0].Date);
    }
}
