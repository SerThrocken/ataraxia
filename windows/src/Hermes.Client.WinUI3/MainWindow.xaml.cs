using Hermes.Client.Core;
using Hermes.Client.WinUI3.Services;
using Hermes.Client.WinUI3.ViewModels;
using Hermes.Client.WinUI3.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Hermes.Client.WinUI3;

public sealed partial class MainWindow : Window, ICommandPaletteHost
{
    private static MainWindow? _current;
    private readonly IConnectionState _connectionState = App.Services.GetRequiredService<IConnectionState>();
    private readonly IGatewayClient _gatewayClient = App.Services.GetRequiredService<IGatewayClient>();
    private readonly TaskbarProgressController _taskbarProgress;
    private IReadOnlyList<SkillInfo>? _skillCache;
    private object? _previouslyFocused;
    private ConnectionState _lastIndicatorState = ConnectionState.Untested;

    /// <summary>Opens the chat page for a session (from the sessions list).</summary>
    public static void NavigateToChat(string sessionId, string? title) =>
        _current?.ContentFrame.Navigate(typeof(ChatPage), (sessionId, title));

    /// <summary>Owner HWND for file pickers and dialogs (WinUI3 pickers require one).</summary>
    public static nint WindowHandle => _current is null
        ? 0
        : WindowNative.GetWindowHandle(_current);

    /// <summary>
    /// Opens Mission Control, tracking the run when a run id is given
    /// (toast deep link). Safe to call before the window is ready.
    /// </summary>
    public static void NavigateToMissionControl(string? runId = null)
    {
        if (_current is null)
        {
            return;
        }

        foreach (var item in _current.NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == "missioncontrol")
            {
                _current.NavView.SelectedItem = item;
                break;
            }
        }

        _current.ContentFrame.Navigate(typeof(MissionControlPage), runId);
    }

    public CommandPaletteViewModel PaletteViewModel { get; }

    /// <summary>Session token/cost meter bound by the status bar (x:Bind source).</summary>
    public UsageMeter Meter { get; } = App.Services.GetRequiredService<UsageMeter>();

    public MainWindow()
    {
        // Created up front; attached to the palette control after
        // InitializeComponent (x:Bind cannot root at Window).
        PaletteViewModel = new CommandPaletteViewModel(this);

        InitializeComponent();
        _current = this;

        // Taskbar progress for long operations (queue flushes, artifact
        // downloads): the tracker is UI-agnostic; this controller drives
        // ITaskbarList3 from the AppWindow handle. Disposed with the window.
        _taskbarProgress = new TaskbarProgressController(
            WindowNative.GetWindowHandle(this),
            App.Services.GetRequiredService<TaskbarProgressTracker>(),
            DispatcherQueue);
        Closed += (_, _) => _taskbarProgress.Dispose();

        // Window-chrome icon (unpackaged apps get no manifest icon).
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "ataraxia.ico");
        if (System.IO.File.Exists(iconPath))
            GetAppWindow()?.SetIcon(iconPath);

        // Attached here, not via x:Bind: the XAML compiler cannot root a
        // compiled binding at Window (it is not a FrameworkElement).
        PaletteControl.ViewModel = PaletteViewModel;
        PaletteViewModel.DismissRequested += (_, _) => DismissPalette();

        SystemBackdrop = new MicaBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        GetAppWindow()?.Resize(new Windows.Graphics.SizeInt32(1200, 800));

        _connectionState.StateChanged += (_, state) =>
            DispatcherQueue.TryEnqueue(() => UpdateConnectionIndicator(state));
        UpdateConnectionIndicator(_connectionState.State);

        NavView.Loaded += async (_, _) =>
        {
            var settings = App.Services.GetRequiredService<SettingsViewModel>();
            await settings.InitializeAsync();

            if (!settings.HasStoredKey)
            {
                // First run: no API key yet — show the connect screen.
                ContentFrame.Navigate(typeof(ConnectPage));
                return;
            }

            NavView.SelectedItem = NavView.MenuItems[0];
            ContentFrame.Navigate(typeof(ChatPage));

            // A key is stored: refresh the app-wide state silently.
            await settings.TestConnectionCommand.ExecuteAsync(null);
        };
    }

    // ---- Command palette (Ctrl+K) ----

    private void OpenPaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenCommandPalette();
    }

    private void OpenCommandPalette()
    {
        _previouslyFocused = FocusManager.GetFocusedElement();
        PaletteControl.Visibility = Visibility.Visible;
        _ = PaletteViewModel.OpenAsync();
        PaletteControl.FocusSearch();
    }

    private void DismissPalette()
    {
        PaletteControl.Visibility = Visibility.Collapsed;
        if (_previouslyFocused is Control focused)
        {
            focused.Focus(FocusState.Programmatic);
        }
    }

    // ---- ICommandPaletteHost ----

    public async Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default)
    {
        // GET /v1/skills 500s today (api-contract.md); the safe fetch degrades
        // to empty and the result is cached for the app's lifetime.
        _skillCache ??= await CommandPaletteEngine.GetSkillsSafeAsync(_gatewayClient, cancellationToken);
        return _skillCache;
    }

    public Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        CommandPaletteEngine.GetSessionsSafeAsync(_gatewayClient, cancellationToken);

    public async Task PerformEffectAsync(PaletteEffect effect)
    {
        switch (effect.Kind)
        {
            case PaletteEffectKind.InsertText:
                InsertIntoComposer(effect.Payload);
                break;
            case PaletteEffectKind.OpenSession:
                NavigateToChat(effect.Payload, effect.Label);
                break;
            case PaletteEffectKind.NavigatePage:
                NavigateToTag(effect.Payload);
                break;
            case PaletteEffectKind.NewSession:
                await NewSessionAsync();
                break;
            case PaletteEffectKind.Reconnect:
                await App.Services.GetRequiredService<SettingsViewModel>().TestConnectionCommand.ExecuteAsync(null);
                break;
        }
    }

    private void InsertIntoComposer(string text)
    {
        NavigateToTag("chat");
        if (ContentFrame.Content is ChatPage chatPage)
        {
            var vm = chatPage.ViewModel;
            vm.InputText = string.IsNullOrWhiteSpace(vm.InputText)
                ? text
                : vm.InputText.TrimEnd() + " " + text;
            chatPage.FocusComposer();
        }
    }

    private async Task NewSessionAsync()
    {
        try
        {
            var session = await _gatewayClient.CreateSessionAsync();
            NavigateToChat(session.Id, string.IsNullOrWhiteSpace(session.Title) ? session.Id : session.Title);
        }
        catch (Exception)
        {
            // Surface connection state instead of failing silently.
            NavigateToTag("settings");
        }
    }

    private void NavigateToTag(string tag)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }

        ContentFrame.Navigate(typeof(ChatPage));
    }

    // ---- Shell ----

    private void UpdateConnectionIndicator(ConnectionState state)
    {
        // Brushes come from system theme resources (never hardcoded), so the
        // dot tracks light/dark/high-contrast themes automatically.
        var (text, brushKey) = state switch
        {
            ConnectionState.Connected => ("Connected", "SystemFillColorSuccessBrush"),
            ConnectionState.Disconnected => ("Disconnected", "SystemFillColorCriticalBrush"),
            _ => ("Not tested", "SystemFillColorNeutralBrush"),
        };
        ConnectionText.Text = text;
        if (Application.Current.Resources.TryGetValue(brushKey, out var value) && value is SolidColorBrush brush)
        {
            ConnectionDot.Fill = brush;
        }

        // Announce state changes to screen readers; the initial paint stays silent.
        if (state != _lastIndicatorState)
        {
            _lastIndicatorState = state;
            var peer = FrameworkElementAutomationPeer.FromElement(ConnectionText);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.ItemAdded,
                AutomationNotificationProcessing.ImportantMostRecent,
                $"Connection status: {text}",
                "ConnectionStatusChanged");
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        var page = tag switch
        {
            "chat" => typeof(ChatPage),
            "sessions" => typeof(SessionsPage),
            "handoff" => typeof(HandoffPage),
            "missioncontrol" => typeof(MissionControlPage),
            "cron" => typeof(CronPage),
            "search" => typeof(SearchPage),
            "models" => typeof(ModelsPage),
            "skills" => typeof(SkillsPage),
            "terminal" => typeof(TerminalBackendsPage),
            "voice" => typeof(VoicePage),
            "memory" => typeof(MemoryPage),
            "messaging" => typeof(MessagingPage),
            "profiles" => typeof(ProfilesPage),
            "health" => typeof(HealthPage),
            "analytics" => typeof(AnalyticsPage),
            "providers" => typeof(ProviderHealthPage),
            "diagnostics" => typeof(DiagnosticsPage),
            "backup" => typeof(BackupPage),
            "updates" => typeof(UpdatesPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(ChatPage),
        };

        ContentFrame.Navigate(page);
    }

    private AppWindow? GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        return AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
    }
}
