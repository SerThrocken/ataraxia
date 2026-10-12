using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// <see cref="HudSnapshot"/> content mapping: dot color and status text for
/// every connection/run combination, active-run selection, and the
/// material-change comparison that gates re-renders.
/// </summary>
public sealed class HudSnapshotTests
{
    private static HudSnapshot Snapshot(
        HudConnectionState connection, params HudRunSummary[] runs) =>
        new(connection, runs, DateTimeOffset.UtcNow);

    [Fact]
    public void UnknownConnectionMapsToUnknownDotAndCheckingText()
    {
        var snapshot = HudSnapshot.Unknown;

        Assert.Equal(HudDotKind.Unknown, snapshot.Dot);
        Assert.Equal("Checking gateway…", snapshot.StatusText);
        Assert.Null(snapshot.ActiveRun);
    }

    [Fact]
    public void ConnectedWithNoRunsIsOk()
    {
        var snapshot = Snapshot(HudConnectionState.Connected);

        Assert.Equal(HudDotKind.Ok, snapshot.Dot);
        Assert.Equal("Gateway connected", snapshot.StatusText);
    }

    [Fact]
    public void ConnectedWithActiveRunIsWorkingAndNamesTheRun()
    {
        var run = new HudRunSummary("run_abc123456789", "running", true, "tool.start");
        var snapshot = Snapshot(HudConnectionState.Connected, run);

        Assert.Equal(HudDotKind.Working, snapshot.Dot);
        Assert.Same(run, snapshot.ActiveRun);
        Assert.Contains("Gateway connected", snapshot.StatusText);
        Assert.Contains("run_abc12345", snapshot.StatusText);
        Assert.Contains("running", snapshot.StatusText);
    }

    [Fact]
    public void DisconnectedIsErrorEvenWithAnActiveRun()
    {
        var run = new HudRunSummary("run_abc", "running", true, null);
        var snapshot = Snapshot(HudConnectionState.Disconnected, run);

        Assert.Equal(HudDotKind.Error, snapshot.Dot);
        Assert.Contains("Gateway unreachable", snapshot.StatusText);
    }

    [Fact]
    public void TerminalRunsAreNotTheActiveRun()
    {
        var done = new HudRunSummary("run_done", "completed", false, null);
        var live = new HudRunSummary("run_live", "running", true, null);
        var snapshot = Snapshot(HudConnectionState.Connected, done, live);

        Assert.Same(live, snapshot.ActiveRun);
        Assert.Equal(HudDotKind.Working, snapshot.Dot);
    }

    [Fact]
    public void AllTerminalRunsFallBackToOk()
    {
        var done = new HudRunSummary("run_done", "completed", false, null);
        var snapshot = Snapshot(HudConnectionState.Connected, done);

        Assert.Null(snapshot.ActiveRun);
        Assert.Equal(HudDotKind.Ok, snapshot.Dot);
        Assert.Equal("Gateway connected", snapshot.StatusText);
    }

    [Fact]
    public void RunSummaryDisplayTextIncludesShortIdStatusAndLastEvent()
    {
        var run = new HudRunSummary("run_abc123456789", "running", true, "tool.start");

        Assert.StartsWith("run_abc12345", run.DisplayText);
        Assert.Contains("running", run.DisplayText);
        Assert.Contains("tool.start", run.DisplayText);
    }

    [Fact]
    public void RunSummaryDisplayTextOmitsMissingLastEvent()
    {
        var run = new HudRunSummary("run_abc", "queued", true, null);

        Assert.Equal("run_abc · queued", run.DisplayText);
    }

    [Fact]
    public void SameContentAsIgnoresTimestamps()
    {
        var a = new HudSnapshot(
            HudConnectionState.Connected,
            [new HudRunSummary("r1", "running", true, null)],
            DateTimeOffset.UtcNow);
        var b = new HudSnapshot(
            HudConnectionState.Connected,
            [new HudRunSummary("r1", "running", true, null)],
            DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.True(a.SameContentAs(b));
        Assert.True(b.SameContentAs(a));
    }

    [Theory]
    [InlineData(HudConnectionState.Connected, HudConnectionState.Disconnected)]
    [InlineData(HudConnectionState.Unknown, HudConnectionState.Connected)]
    public void SameContentAsDetectsConnectionChange(
        HudConnectionState first, HudConnectionState second)
    {
        var a = Snapshot(first);
        var b = Snapshot(second);

        Assert.False(a.SameContentAs(b));
    }

    [Fact]
    public void SameContentAsDetectsRunStatusChange()
    {
        var a = Snapshot(HudConnectionState.Connected,
            new HudRunSummary("r1", "running", true, null));
        var b = Snapshot(HudConnectionState.Connected,
            new HudRunSummary("r1", "waiting_for_approval", true, null));

        Assert.False(a.SameContentAs(b));
    }

    [Fact]
    public void SameContentAsDetectsRunListChange()
    {
        var a = Snapshot(HudConnectionState.Connected,
            new HudRunSummary("r1", "running", true, null));
        var b = Snapshot(HudConnectionState.Connected,
            new HudRunSummary("r1", "running", true, null),
            new HudRunSummary("r2", "queued", true, null));

        Assert.False(a.SameContentAs(b));
    }

    [Fact]
    public void SameContentAsHandlesNull()
    {
        Assert.False(Snapshot(HudConnectionState.Connected).SameContentAs(null));
    }
}
