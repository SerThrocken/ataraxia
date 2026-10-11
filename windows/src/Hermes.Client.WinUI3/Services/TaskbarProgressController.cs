using System.Runtime.InteropServices;
using Hermes.Client.Core;
using Microsoft.UI.Dispatching;

namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// Drives the Windows taskbar progress bar (ITaskbarList3) from the
/// UI-agnostic <see cref="TaskbarProgressTracker"/>. Mapping:
/// <c>None</c> → TBPF_NOPROGRESS (bar cleared), <c>Indeterminate</c> →
/// TBPF_INDETERMINATE, <c>Determinate</c> → TBPF_NORMAL with the fraction
/// scaled to per-mille. All interop calls run on the UI thread; the COM
/// object is created lazily on first use.
/// </summary>
public sealed class TaskbarProgressController : IDisposable
{
    private readonly nint _hwnd;
    private readonly TaskbarProgressTracker _tracker;
    private readonly DispatcherQueue _dispatcher;
    private ITaskbarList3? _taskbar;
    private bool _disposed;

    public TaskbarProgressController(nint hwnd, TaskbarProgressTracker tracker, DispatcherQueue dispatcher)
    {
        _hwnd = hwnd;
        _tracker = tracker;
        _dispatcher = dispatcher;
        _tracker.Changed += OnChanged;
        Apply(tracker.Current);
    }

    private void OnChanged(object? sender, TaskbarProgress progress) =>
        _dispatcher.TryEnqueue(() => Apply(progress));

    private void Apply(TaskbarProgress progress)
    {
        if (_disposed)
        {
            return;
        }

        _taskbar ??= CreateTaskbar();
        switch (progress.Kind)
        {
            case TaskbarProgressKind.None:
                _taskbar.SetProgressState(_hwnd, TbpFlag.NoProgress);
                break;
            case TaskbarProgressKind.Indeterminate:
                _taskbar.SetProgressState(_hwnd, TbpFlag.Indeterminate);
                break;
            default:
                _taskbar.SetProgressState(_hwnd, TbpFlag.Normal);
                _taskbar.SetProgressValue(_hwnd, (ulong)(progress.Fraction * 1000), 1000);
                break;
        }
    }

    private static ITaskbarList3 CreateTaskbar()
    {
        var taskbar = (ITaskbarList3)new TaskbarList();
        taskbar.HrInit();
        return taskbar;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tracker.Changed -= OnChanged;
        if (_taskbar is not null)
        {
            Marshal.ReleaseComObject(_taskbar);
            _taskbar = null;
        }
    }

    private enum TbpFlag
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(nint hwnd);
        void DeleteTab(nint hwnd);
        void ActivateTab(nint hwnd);
        void SetActiveAlt(nint hwnd);
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(nint hwnd, ulong completed, ulong total);
        void SetProgressState(nint hwnd, TbpFlag flags);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList
    {
    }
}
