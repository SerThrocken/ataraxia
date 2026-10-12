using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Microsoft.UI.Dispatching;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Companion-HUD view model. Applies <see cref="HudSnapshot"/>s published by
/// <see cref="HudMonitor"/> on the UI thread and exposes the three quick
/// actions — wired to the exact handlers the tray menu uses
/// (<see cref="MainWindow.NewChatAsync"/>, <see cref="MainWindow.ToggleVisibility"/>,
/// <see cref="MainWindow.NavigateToMissionControl"/>), so the surfaces cannot drift.
/// </summary>
public sealed partial class HudViewModel : ObservableObject, IDisposable
{
    private readonly HudMonitor _monitor;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<Task> _newChat;
    private readonly Action _toggleMainWindow;
    private readonly Action<string?> _openMissionControl;
    private readonly Action _requestClose;
    private bool _disposed;

    public HudViewModel(
        HudMonitor monitor,
        DispatcherQueue dispatcher,
        Func<Task> newChat,
        Action toggleMainWindow,
        Action<string?> openMissionControl,
        Action requestClose)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(newChat);
        ArgumentNullException.ThrowIfNull(toggleMainWindow);
        ArgumentNullException.ThrowIfNull(openMissionControl);
        ArgumentNullException.ThrowIfNull(requestClose);
        _monitor = monitor;
        _dispatcher = dispatcher;
        _newChat = newChat;
        _toggleMainWindow = toggleMainWindow;
        _openMissionControl = openMissionControl;
        _requestClose = requestClose;
        _monitor.SnapshotChanged += OnSnapshotChanged;
    }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Checking gateway…";

    [ObservableProperty]
    public partial HudDotKind DotKind { get; set; } = HudDotKind.Unknown;

    [ObservableProperty]
    public partial string RunText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasActiveRun { get; set; }

    [ObservableProperty]
    public partial double Opacity { get; set; } = HudSettings.DefaultOpacity;

    [ObservableProperty]
    public partial bool ClickThrough { get; set; }

    /// <summary>Run id for the Mission Control deep link; null when idle.</summary>
    public string? ActiveRunId { get; private set; }

    /// <summary>
    /// Applies a snapshot. Called on the UI thread (the monitor raises on its
    /// polling thread; <see cref="OnSnapshotChanged"/> marshals here).
    /// </summary>
    public void ApplySnapshot(HudSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(_disposed, this);
        StatusText = snapshot.StatusText;
        DotKind = snapshot.Dot;
        var run = snapshot.ActiveRun;
        ActiveRunId = run?.RunId;
        HasActiveRun = run is not null;
        RunText = run?.DisplayText ?? string.Empty;
    }

    public void StartMonitoring() => _monitor.Start();

    public Task StopMonitoringAsync() => _monitor.StopAsync();

    private void OnSnapshotChanged(object? sender, HudSnapshot snapshot) =>
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                ApplySnapshot(snapshot);
            }
        });

    [RelayCommand]
    private Task NewChatAsync() => _newChat();

    [RelayCommand]
    private void ToggleMainWindow() => _toggleMainWindow();

    /// <summary>Opens Mission Control, deep-linking the active run when there is one.</summary>
    [RelayCommand]
    private void OpenMissionControl() => _openMissionControl(ActiveRunId);

    [RelayCommand]
    private void CloseHud() => _requestClose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.SnapshotChanged -= OnSnapshotChanged;
    }
}
