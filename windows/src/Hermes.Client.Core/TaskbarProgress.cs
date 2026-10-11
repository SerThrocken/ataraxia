namespace Hermes.Client.Core;

/// <summary>Taskbar progress state: none, indeterminate, or determinate (0..1).</summary>
public enum TaskbarProgressKind
{
    None,
    Indeterminate,
    Determinate,
}

/// <summary>Immutable taskbar-progress snapshot published by the tracker.</summary>
public sealed record TaskbarProgress(TaskbarProgressKind Kind, double Fraction)
{
    public static readonly TaskbarProgress None = new(TaskbarProgressKind.None, 0);

    public static TaskbarProgress Indeterminate() => new(TaskbarProgressKind.Indeterminate, 0);

    public static TaskbarProgress Determinate(double fraction) =>
        new(TaskbarProgressKind.Determinate, Math.Clamp(fraction, 0, 1));
}

/// <summary>
/// One tracked long operation. Starts indeterminate; <see cref="Report"/>
/// switches it determinate. Disposing ends the operation.
/// </summary>
public interface ITaskbarProgressOperation : IDisposable
{
    void ReportIndeterminate();
    void Report(double fraction);
}

/// <summary>
/// UI-agnostic taskbar-progress state machine (no HWND, no interop — the
/// WinUI controller subscribes to <see cref="Changed"/> and drives
/// ITaskbarList3). Aggregation: no active operations → <c>None</c>; any
/// determinate operation → <c>Determinate</c> with the mean of determinate
/// fractions; otherwise <c>Indeterminate</c>. <see cref="Changed"/> fires only
/// when the snapshot actually changes. Thread-safe.
/// </summary>
public sealed class TaskbarProgressTracker
{
    private readonly object _gate = new();
    private readonly HashSet<TrackedOperation> _operations = new();

    public event EventHandler<TaskbarProgress>? Changed;

    public TaskbarProgress Current { get; private set; } = TaskbarProgress.None;

    public ITaskbarProgressOperation BeginOperation()
    {
        var op = new TrackedOperation(this);
        lock (_gate)
        {
            _operations.Add(op);
        }

        Recalculate();
        return op;
    }

    private void EndOperation(TrackedOperation op)
    {
        lock (_gate)
        {
            _operations.Remove(op);
        }

        Recalculate();
    }

    private void Recalculate()
    {
        double[] determinate;
        lock (_gate)
        {
            determinate = _operations
                .Where(o => o.Kind == TaskbarProgressKind.Determinate)
                .Select(o => o.Fraction)
                .ToArray();
            var hasAny = _operations.Count > 0;
            var next = !hasAny
                ? TaskbarProgress.None
                : determinate.Length > 0
                    ? TaskbarProgress.Determinate(determinate.Average())
                    : TaskbarProgress.Indeterminate();
            if (next == Current)
            {
                return;
            }

            Current = next;
        }

        Changed?.Invoke(this, Current);
    }

    private sealed class TrackedOperation(TaskbarProgressTracker tracker) : ITaskbarProgressOperation
    {
        private bool _disposed;

        public TaskbarProgressKind Kind { get; private set; } = TaskbarProgressKind.Indeterminate;
        public double Fraction { get; private set; }

        public void ReportIndeterminate()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Kind = TaskbarProgressKind.Indeterminate;
            tracker.Recalculate();
        }

        public void Report(double fraction)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Kind = TaskbarProgressKind.Determinate;
            Fraction = Math.Clamp(fraction, 0, 1);
            tracker.Recalculate();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            tracker.EndOperation(this);
        }
    }
}
