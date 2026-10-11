using Xunit;
using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class UsageMeterTests
{
    private static SseEvent RunCompletedEvent(long inputTokens, long outputTokens)
    {
        var json = $"{{\"session_id\":\"s1\",\"run_id\":\"r1\",\"seq\":6,\"ts\":1791734811.6," +
            $"\"usage\":{{\"input_tokens\":{inputTokens},\"output_tokens\":{outputTokens}," +
            $"\"total_tokens\":{inputTokens + outputTokens}}}}}";
        return new SseEvent("run.completed", JsonDocument.Parse(json), "s1", "r1", 6, 1791734811.6);
    }

    [Fact]
    public void FromEventParsesContractRunCompletedPayload()
    {
        using var e = RunCompletedEvent(29080, 5);

        var turn = TurnUsage.FromEvent(e, 1);

        Assert.NotNull(turn);
        Assert.Equal(1, turn.TurnNumber);
        Assert.Equal(29080, turn.InputTokens);
        Assert.Equal(5, turn.OutputTokens);
        Assert.Equal(29085, turn.TotalTokens);
    }

    [Fact]
    public void FromEventReturnsNullWithoutUsage()
    {
        using var e = new SseEvent("run.completed",
            JsonDocument.Parse("""{"session_id":"s1","run_id":"r1","seq":6,"ts":1.0}"""),
            "s1", "r1", 6, 1.0);

        Assert.Null(TurnUsage.FromEvent(e, 1));
    }

    [Fact]
    public void FromEventReturnsNullForOtherEvents()
    {
        using var e = new SseEvent("assistant.delta",
            JsonDocument.Parse("""{"delta":"ok"}"""), "s1", "r1", 3, 1.0);

        Assert.Null(TurnUsage.FromEvent(e, 1));
    }

    [Fact]
    public void BeginSessionSeedsTotalsAndCost()
    {
        var meter = new UsageMeter();

        meter.BeginSession("s1", 15708014, 462663, 5.488759025);

        Assert.Equal("s1", meter.SessionId);
        Assert.Equal(15708014, meter.InputTokens);
        Assert.Equal(462663, meter.OutputTokens);
        Assert.Equal(16170677, meter.TotalTokens);
        Assert.Equal(5.488759025, meter.EstimatedCostUsd);
        Assert.Empty(meter.Turns);
    }

    [Fact]
    public void BeginSessionResetsPreviousState()
    {
        var meter = new UsageMeter();
        meter.BeginSession("s1", 100, 10, 0.01);
        using var e = RunCompletedEvent(50, 5);
        meter.RecordTurn(TurnUsage.FromEvent(e, 1)!);

        meter.BeginSession("s2", 0, 0, null);

        Assert.Equal("s2", meter.SessionId);
        Assert.Equal(0, meter.TotalTokens);
        Assert.Null(meter.EstimatedCostUsd);
        Assert.Empty(meter.Turns);
    }

    [Fact]
    public void RecordTurnAccumulatesTokens()
    {
        var meter = new UsageMeter();
        meter.BeginSession("s1", 1000, 100, 0.05);

        using var e1 = RunCompletedEvent(29080, 5);
        meter.RecordTurn(TurnUsage.FromEvent(e1, meter.Turns.Count + 1)!);
        using var e2 = RunCompletedEvent(30000, 12);
        meter.RecordTurn(TurnUsage.FromEvent(e2, meter.Turns.Count + 1)!);

        Assert.Equal(2, meter.Turns.Count);
        Assert.Equal(1, meter.Turns[0].TurnNumber);
        Assert.Equal(2, meter.Turns[1].TurnNumber);
        Assert.Equal(60080, meter.InputTokens);
        Assert.Equal(117, meter.OutputTokens);
        Assert.Equal(60197, meter.TotalTokens);
    }

    [Fact]
    public void SyncFromSessionAdoptsAuthoritativeTotalsWithoutDoubleCounting()
    {
        var meter = new UsageMeter();
        meter.BeginSession("s1", 1000, 100, 0.05);
        using var e = RunCompletedEvent(29080, 5);
        meter.RecordTurn(TurnUsage.FromEvent(e, meter.Turns.Count + 1)!);

        meter.SyncFromSession(new Session("s1", null, null, 0, null, null, null, null, false, false,
            30080, 105, 0.06123));

        Assert.Equal(30080, meter.InputTokens);
        Assert.Equal(105, meter.OutputTokens);
        Assert.Equal(0.06123, meter.EstimatedCostUsd);
        // Per-turn breakdown is the client's own record; the session sync must not touch it.
        Assert.Single(meter.Turns);
    }

    [Fact]
    public void SyncFromSessionIgnoresOtherSession()
    {
        var meter = new UsageMeter();
        meter.BeginSession("s1", 1000, 100, 0.05);

        meter.SyncFromSession(new Session("s2", null, null, 0, null, null, null, null, false, false,
            999, 999, 9.99));

        Assert.Equal(1000, meter.InputTokens);
        Assert.Equal(0.05, meter.EstimatedCostUsd);
    }

    [Fact]
    public void SummaryTextFormatsTokensAndCost()
    {
        var meter = new UsageMeter();
        meter.BeginSession("s1", 15708014, 462663, 5.488759025);

        Assert.Equal("16,170,677 tokens · $5.4888", meter.SummaryText);
        Assert.Contains("16,170,677 tokens", meter.ScreenReaderText);
        Assert.Contains("$5.4888", meter.ScreenReaderText);

        meter.BeginSession("s2", 100, 5, null);
        Assert.Equal("105 tokens", meter.SummaryText);
    }

    [Fact]
    public void RecordTurnRaisesPropertyChanged()
    {
        var meter = new UsageMeter();
        meter.BeginSession("s1", 0, 0, null);
        var changed = new List<string?>();
        meter.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        using var e = RunCompletedEvent(10, 2);
        meter.RecordTurn(TurnUsage.FromEvent(e, 1)!);

        Assert.Contains(nameof(UsageMeter.TotalTokens), changed);
        Assert.Contains(nameof(UsageMeter.InputTokens), changed);
        Assert.Contains(nameof(UsageMeter.OutputTokens), changed);
    }
}
