using Hermes.Client.Core;
using Hermes.Client.WinUI3.ViewModels;
using Microsoft.UI.Dispatching;

namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// Owns the companion-HUD window lifecycle. Opt-in only: the overlay is
/// created solely from an explicit user action (Settings toggle, tray menu,
/// or hotkey) and never appears on its own. Closing the window stops the
/// monitor — no orphan windows, no background polling after close.
/// All HUD settings live in the "hud" blob via <see cref="ISettingsService"/>.
/// </summary>
public sealed class HudService
{
    public const string SettingsName = "hud";

    private readonly IGatewayClient _client;
    private readonly ISettingsService _settings;
    private readonly RunToastService _toasts;

    private DispatcherQueue _dispatcher = null!;
    private Func<Task>? _newChat;
    private Action? _toggleMainWindow;
    private Action<string?>? _openMissionControl;

    private HudWindow? _window;
    private HudViewModel? _viewModel;
    private HudMonitor? _monitor;
    private HudSettings _settingsCache = new();
    private Task _lastPersist = Task.CompletedTask;
    private bool _initialized;
    private bool _shutdown;

    public HudService(IGatewayClient client, ISettingsService settings, RunToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(toasts);
        _client = client;
        _settings = settings;
        _toasts = toasts;
    }

    public bool IsVisible => _window is not null;

    /// <summary>
    /// Loads the saved settings and subscribes to run events. The synchronous
    /// prefix runs on the UI thread (call from OnLaunched); showing a
    /// remembered opt-in is marshaled. Quick actions are the tray menu's own
    /// handlers, passed in by the app.
    /// </summary>
    public async Task InitializeAsync(
        Func<Task> newChat,
        Action toggleMainWindow,
        Action<string?> openMissionControl)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _newChat = newChat;
        _toggleMainWindow = toggleMainWindow;
        _openMissionControl = openMissionControl;

        _settingsCache =
            (await _settings.LoadAsync<HudSettings>(SettingsName).ConfigureAwait(false))
            ?.Normalized() ?? new HudSettings();

        _toasts.ApprovalRequested += OnApprovalRequested;
        _toasts.RunCompleted += OnRunCompleted;

        if (_settingsCache.Enabled)
        {
            _dispatcher.TryEnqueue(Show);
        }
    }

    /// <summary>UI-thread only. Creates the window + monitor and starts polling.</summary>
    public void Show()
    {
        if (_shutdown || !_initialized || _window is not null)
        {
            return;
        }

        _monitor = new HudMonitor(_client);
        _viewModel = new HudViewModel(
            _monitor,
            _dispatcher,
            _newChat!,
            _toggleMainWindow!,
            _openMissionControl!,
            requestClose: Hide);
        _viewModel.Opacity = _settingsCache.Opacity;
        _viewModel.ClickThrough = _settingsCache.ClickThrough;
        _viewModel.ApplySnapshot(_monitor.Latest);

        _window = new HudWindow(_viewModel);
        if (_settingsCache.HasSavedPosition)
        {
            _window.MoveTo(_settingsCache.Left!.Value, _settingsCache.Top!.Value);
        }
        else
        {
            _window.MoveToDefault();
        }

        _window.Closed += OnWindowClosed;
        _window.ShowWithoutActivation();
        _viewModel.StartMonitoring();
    }

    /// <summary>UI-thread only. Closes the window and stops all polling.</summary>
    public void Hide()
    {
        var window = _window;
        if (window is null)
        {
            return;
        }

        Teardown();
        window.Close(); // Closed fires; Teardown there is a no-op.
    }

    /// <summary>Thread-safe toggle for the tray menu and the global hotkey.</summary>
    public void Toggle()
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (_shutdown || !_initialized)
            {
                return;
            }

            SetVisible(_window is null);
        });
    }

    /// <summary>Master switch from Settings: persists, then shows or hides explicitly.</summary>
    public async Task SetEnabledAsync(bool enabled)
    {
        _settingsCache = _settingsCache with { Enabled = enabled };
        await SaveSettingsAsync().ConfigureAwait(false);
        _dispatcher.TryEnqueue(() =>
        {
            if (!_shutdown && _initialized)
            {
                SetVisible(enabled);
            }
        });
    }

    /// <summary>UI-thread only. Explicit show/hide (unlike <see cref="Toggle"/>).</summary>
    private void SetVisible(bool visible)
    {
        if (visible)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    /// <summary>Opacity + click-through from Settings: persists and live-applies.</summary>
    public async Task ApplyAppearanceAsync(double opacity, bool clickThrough)
    {
        _settingsCache = _settingsCache with
        {
            Opacity = HudSettings.ClampOpacity(opacity),
            ClickThrough = clickThrough,
        };
        await SaveSettingsAsync().ConfigureAwait(false);
        _dispatcher.TryEnqueue(() =>
        {
            if (_viewModel is not null)
            {
                _viewModel.Opacity = _settingsCache.Opacity;
                _viewModel.ClickThrough = clickThrough;
            }
        });
    }

    /// <summary>Persists the toggle-hotkey text (registration is the caller's job).</summary>
    public async Task SetHotkeyAsync(string hotkeyText)
    {
        _settingsCache = _settingsCache with { Hotkey = hotkeyText.Trim() };
        await SaveSettingsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// App shutdown: unsubscribes, persists the final position, closes the
    /// window, and stops the monitor. Awaits the settings write so the
    /// position isn't lost on a fast exit.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _toasts.ApprovalRequested -= OnApprovalRequested;
        _toasts.RunCompleted -= OnRunCompleted;

        var window = _window;
        Teardown();
        if (window is not null)
        {
            _dispatcher.TryEnqueue(window.Close);
        }

        await _lastPersist.ConfigureAwait(false);
    }

    private void OnWindowClosed(object? sender, object e) => Teardown();

    /// <summary>
    /// Idempotent teardown: detaches the window, persists position/appearance,
    /// disposes the view model, and stops the monitor. Never throws — teardown
    /// runs on window-close and shutdown paths.
    /// </summary>
    private void Teardown()
    {
        var window = _window;
        var viewModel = _viewModel;
        var monitor = _monitor;
        _window = null;
        _viewModel = null;
        _monitor = null;

        if (window is null)
        {
            return;
        }

        window.Closed -= OnWindowClosed;
        _lastPersist = PersistStateAsync(window, viewModel);
        viewModel?.Dispose();
        _ = StopMonitorAsync(monitor);
    }

    private async Task PersistStateAsync(HudWindow window, HudViewModel? viewModel)
    {
        var (x, y) = window.GetPosition();
        _settingsCache = _settingsCache with
        {
            Left = x,
            Top = y,
            Opacity = viewModel?.Opacity ?? _settingsCache.Opacity,
            ClickThrough = viewModel?.ClickThrough ?? _settingsCache.ClickThrough,
        };

        try
        {
            await _settings.SaveAsync(SettingsName, _settingsCache).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A settings-write failure must not break teardown.
        }
    }

    private static async Task StopMonitorAsync(HudMonitor? monitor)
    {
        if (monitor is null)
        {
            return;
        }

        monitor.UntrackAll();
        try
        {
            await monitor.StopAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Teardown path: stopping must not throw.
        }

        monitor.Dispose();
    }

    private Task SaveSettingsAsync() =>
        _settings.SaveAsync(SettingsName, _settingsCache);

    private void OnApprovalRequested(object? sender, ApprovalRequestedEventArgs e)
    {
        var monitor = _monitor;
        if (monitor is null)
        {
            return;
        }

        try
        {
            monitor.TrackRun(e.RunId);
            _ = monitor.RefreshOnceAsync();
        }
        catch (ObjectDisposedException)
        {
            // Raced with teardown; the next Show starts fresh.
        }
    }

    private void OnRunCompleted(object? sender, RunStatus status) =>
        _monitor?.UntrackRun(status.RunId);
}
