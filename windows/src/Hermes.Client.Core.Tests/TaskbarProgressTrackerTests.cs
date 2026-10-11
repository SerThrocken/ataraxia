using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// TDD for the taskbar-progress state machine. Pure logic: no HWND, no
/// ITaskbarList3 — the WinUI controller subscribes to <see cref="TaskbarProgressTracker.Changed"/>
/// and does the interop. Aggregation rule: determinate ops win (mean of their
/// fractions); otherwise any active op shows indeterminate; no ops shows none.
/// </summary>
public sealed class TaskbarProgressTrackerTests
{
    [Fact]
    public void StartsWithNoProgress()
    {
        var tracker = new TaskbarProgressTracker();

        Assert.Equal(TaskbarProgressKind.None, tracker.Current.Kind);
    }

    [Fact]
    public void BeginOperationShowsIndeterminate()
    {
        var tracker = new TaskbarProgressTracker();
        var seen = new List<TaskbarProgress>();
        tracker.Changed += (_, p) => seen.Add(p);

        using var op = tracker.BeginOperation();

        Assert.Equal(TaskbarProgressKind.Indeterminate, tracker.Current.Kind);
        Assert.Equal(TaskbarProgressKind.Indeterminate, Assert.Single(seen).Kind);
    }

    [Fact]
    public void ReportSwitchesToDeterminate()
    {
        var tracker = new TaskbarProgressTracker();
        using var op = tracker.BeginOperation();

        op.Report(0.25);

        Assert.Equal(TaskbarProgressKind.Determinate, tracker.Current.Kind);
        Assert.Equal(0.25, tracker.Current.Fraction, precision: 9);
    }

    [Fact]
    public void ReportClampsFraction()
    {
        var tracker = new TaskbarProgressTracker();
        using var op = tracker.BeginOperation();

        op.Report(2.0);
        Assert.Equal(1.0, tracker.Current.Fraction, precision: 9);

        op.Report(-0.5);
        Assert.Equal(0.0, tracker.Current.Fraction, precision: 9);
    }

    [Fact]
    public void DeterminateOperationWinsOverIndeterminate()
    {
        var tracker = new TaskbarProgressTracker();
        using var slow = tracker.BeginOperation();
        using var fast = tracker.BeginOperation();

        fast.Report(0.5);

        Assert.Equal(TaskbarProgressKind.Determinate, tracker.Current.Kind);
        Assert.Equal(0.5, tracker.Current.Fraction, precision: 9);
    }

    [Fact]
    public void MultipleDeterminateOperationsAverage()
    {
        var tracker = new TaskbarProgressTracker();
        using var first = tracker.BeginOperation();
        using var second = tracker.BeginOperation();

        first.Report(0.2);
        second.Report(0.6);

        Assert.Equal(TaskbarProgressKind.Determinate, tracker.Current.Kind);
        Assert.Equal(0.4, tracker.Current.Fraction, precision: 9);
    }

    [Fact]
    public void DisposingAllOperationsClearsProgress()
    {
        var tracker = new TaskbarProgressTracker();
        var seen = new List<TaskbarProgress>();
        tracker.Changed += (_, p) => seen.Add(p);

        var op = tracker.BeginOperation();
        op.Report(0.9);
        op.Dispose();

        Assert.Equal(TaskbarProgressKind.None, tracker.Current.Kind);
        Assert.Equal(TaskbarProgressKind.None, seen[^1].Kind);
    }

    [Fact]
    public void DisposingOneOfTwoKeepsTheOther()
    {
        var tracker = new TaskbarProgressTracker();
        using var keep = tracker.BeginOperation();
        var drop = tracker.BeginOperation();
        keep.Report(0.3);

        drop.Dispose();

        Assert.Equal(TaskbarProgressKind.Determinate, tracker.Current.Kind);
        Assert.Equal(0.3, tracker.Current.Fraction, precision: 9);
    }

    [Fact]
    public void ChangedFiresOnlyOnActualChange()
    {
        var tracker = new TaskbarProgressTracker();
        var seen = new List<TaskbarProgress>();
        tracker.Changed += (_, p) => seen.Add(p);
        using var op = tracker.BeginOperation();

        op.Report(0.5);
        op.Report(0.5); // same snapshot: no event
        op.ReportIndeterminate();
        op.ReportIndeterminate(); // same snapshot: no event

        Assert.Equal(
            new[] { TaskbarProgressKind.Indeterminate, TaskbarProgressKind.Determinate, TaskbarProgressKind.Indeterminate },
            seen.Select(p => p.Kind));
    }
}
