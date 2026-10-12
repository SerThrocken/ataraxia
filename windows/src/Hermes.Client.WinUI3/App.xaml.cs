using Hermes.Client.Core;
using Hermes.Client.Infrastructure;
using Hermes.Client.WinUI3.Services;
using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Velopack;

namespace Hermes.Client.WinUI3;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Velopack install/update/uninstall hooks: must run first. During
        // those operations the hook executes and the process exits from
        // inside Run(), so normal startup below only runs on a real launch.
        // No-op in dev/debug builds (not a Velopack install).
        VelopackApp.Build().Run();

        var services = new ServiceCollection();

        // Tailscale-first: the gateway binds 0.0.0.0:8642 on the Acer;
        // loopback is the fallback when running on the Acer itself.
        services.AddSingleton(new GatewayOptions { BaseUrl = new Uri("http://100.101.250.118:8642") });
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IConnectionState, ConnectionStateService>();
        services.AddSingleton<ConnectionTester>();

        // Offline queue: durable JSONL store + FIFO flusher over
        // POST /api/sessions/{id}/chat. Singletons: the queue outlives any
        // one chat page, so nothing is lost across navigation or restarts.
        services.AddSingleton<IOutboundMessageQueue, FileOutboundMessageQueue>();
        services.AddSingleton<OutboundQueueFlusher>();

        // Camera-to-agent (WinRT MediaCapture, no new dependency) and the
        // UI-agnostic taskbar-progress tracker both live as singletons: the
        // camera service holds no state between captures, and progress from
        // any page funnels into one taskbar bar.
        services.AddSingleton<ICameraCaptureService, MediaCaptureCameraService>();
        services.AddSingleton<TaskbarProgressTracker>();

        // Shared gateway client (chat worker): concrete GatewayClient over the
        // shared GatewayClientBase — no HTTP/auth duplication. The connection
        // flow owns the key/URL UX; the client is pointed at the saved values
        // once they load (InitializeGatewayClientAsync below).
        services.AddSingleton<IGatewayClient>(sp =>
            new GatewayClient(new HttpClient(), sp.GetRequiredService<GatewayOptions>()));

        // Session token/cost meter: singleton so the MainWindow status bar and
        // every transient ChatViewModel share one per-session meter.
        services.AddSingleton<UsageMeter>();

        services.AddTransient<ChatViewModel>();
        services.AddTransient<SessionsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<ModelsViewModel>();
        services.AddTransient<SkillsViewModel>();
        services.AddTransient<TerminalBackendsViewModel>();
        services.AddTransient<VoiceViewModel>();
        services.AddTransient<MemoryViewModel>();
        services.AddTransient<MessagingViewModel>();
        services.AddTransient<ProfilesViewModel>();
        services.AddTransient<CronViewModel>();

        // Gold ops dashboards (Ataraxia worker C): transient view models for
        // the health / analytics / provider / diagnostics pages.
        services.AddTransient<HealthViewModel>();
        services.AddTransient<AnalyticsViewModel>();
        services.AddTransient<ProviderHealthViewModel>();
        services.AddTransient<DiagnosticsViewModel>();

        // Backup/restore (worker F): the gateway exposes no backup API, so
        // the page backs up this client's own settings blob + API key via
        // the versioned ClientSettingsBackup (Core, unit-tested).
        services.AddTransient(sp => new ClientSettingsBackup(
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ISecretStore>(),
            SettingsViewModel.SettingsName,
            SettingsViewModel.ApiKeySecretName));
        services.AddTransient<BackupViewModel>();

        // Self-updates (Velopack, MIT — see DEPENDENCIES.md). No update feed
        // is published yet, so FeedUrl stays null and the update page labels
        // self-updates honestly unavailable. Set it when a feed exists;
        // do NOT invent a feed URL.
        services.AddSingleton(new AppUpdateOptions { FeedUrl = null });
        services.AddSingleton<IAppUpdater, VelopackAppUpdater>();
        services.AddTransient<UpdateCoordinator>();
        services.AddTransient<UpdatesViewModel>();

        // Session handoff QR (Ataraxia worker E): in-memory single-use token
        // store (singleton — outstanding tokens outlive any one page),
        // QRCoder-backed renderer, and the transient page/dialog view models.
        services.AddSingleton<HandoffTokenStore>();
        services.AddSingleton<IQrCodeRenderer, QrCodeRenderer>();
        services.AddTransient<HandoffViewModel>();
        services.AddTransient<ScheduleDialogViewModel>();

        // Diff view (worker E): the chat surface feeds parsed tool.complete
        // inline_diff payloads into this registry; DiffViewer reads it via
        // IDiffSource. Singleton: diffs outlive navigation.
        services.AddSingleton<ToolDiffRegistry>();
        services.AddSingleton<IDiffSource>(sp => sp.GetRequiredService<ToolDiffRegistry>());

        // Electron import: reads the Hermes desktop app's saved connections
        // and settings (read-only scan) and migrates the chosen ones — URL
        // into the settings blob, API key straight into DPAPI. Registered
        // before the view model so the import screen can resolve it.
        services.AddSingleton(sp => new ElectronImporter(
            ElectronDataPaths.Default,
            sp.GetRequiredService<ISecretStore>(),
            sp.GetRequiredService<ISettingsService>(),
            SettingsViewModel.ApiKeySecretName,
            SettingsViewModel.SettingsName));
        services.AddTransient<ImportViewModel>();

        // Singleton: shared by the first-run ConnectPage and the Settings page.
        services.AddSingleton<SettingsViewModel>();

        // Mission Control: run toasts (singleton: the activator outlives any
        // page) and the per-navigation view model.
        services.AddSingleton<Services.RunToastService>();
        services.AddTransient<MissionControlViewModel>();

        // System tray (H.NotifyIcon.WinUI, MIT) and the global quick-entry
        // hotkey (Win32 RegisterHotKey via CsWin32). Both are singletons:
        // the tray icon and the hotkey registration outlive any one window.
        // The hotkey service is resolved on the UI thread in OnLaunched —
        // its hidden message window must be created there.
        services.AddSingleton<TrayService>();
        services.AddSingleton<IHotkeyRegistrar, GlobalHotkeyService>();

        Services = services.BuildServiceProvider();

        _window = new MainWindow();
        _window.Activate();

        // ataraxia:// deep links (PowerToys Run plugin, scripts): register the
        // protocol, then honor a protocol activation if this launch was one.
        DeepLinkService.EnsureProtocolRegistered();
        DeepLinkService.HandleLaunchActivation(_window);

        // Toast activation: approve/deny hit POST /v1/runs/{id}/approval,
        // then every toast deep-links back to the run in Mission Control.
        var toasts = Services.GetRequiredService<Services.RunToastService>();
        toasts.EnsureRegistered();
        Microsoft.Windows.AppNotifications.AppNotificationManager.Default.NotificationInvoked +=
            (sender, toastArgs) => _ = HandleToastActivationAsync(toastArgs.Argument);

        // System tray: context menu (show/hide, new chat, Mission Control,
        // quit). The approval balloon rides the toast service's trigger
        // point, so both surfaces fire together.
        var tray = Services.GetRequiredService<TrayService>();
        tray.Start(
            toggleWindow: MainWindow.ToggleVisibility,
            newChat: MainWindow.NewChatAsync,
            missionControl: () => MainWindow.NavigateToMissionControl(),
            quit: () => Application.Current.Exit());
        toasts.ApprovalRequested += (_, e) => tray.ShowApprovalBalloon(e.RunId, e.Request);
        _window.Closed += (_, _) =>
        {
            tray.Dispose();
            Services.GetRequiredService<IHotkeyRegistrar>().Dispose();
        };

        // Global quick-entry hotkey: summon the box from anywhere. The
        // binding itself is registered by SettingsViewModel once the saved
        // value loads; this subscription only opens the window.
        var hotkeys = Services.GetRequiredService<IHotkeyRegistrar>();
        hotkeys.Pressed += (_, _) => _window.DispatcherQueue.TryEnqueue(OpenQuickEntry);

        _ = MaybeShowTrayHintAsync();
        _ = InitializeGatewayClientAsync(Services);
    }

    private QuickEntryWindow? _quickEntry;

    private void OpenQuickEntry()
    {
        if (_quickEntry is not null)
        {
            _quickEntry.Activate();
            return;
        }

        _quickEntry = new QuickEntryWindow();
        _quickEntry.Closed += (_, _) => _quickEntry = null;
        _quickEntry.Activate();
    }

    /// <summary>
    /// One-time hint: Windows 11 hides new tray icons by default, so without
    /// this the tray icon looks broken on first launch.
    /// </summary>
    private async Task MaybeShowTrayHintAsync()
    {
        var settings = Services.GetRequiredService<ISettingsService>();
        var tray = await settings.LoadAsync<TraySettings>(SettingsViewModel.TraySettingsName)
            ?? new TraySettings();
        var xamlRoot = (_window?.Content as FrameworkElement)?.XamlRoot;
        if (tray.TrayHintShown || xamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Ataraxia lives in the system tray",
            Content = "Windows 11 hides new tray icons by default. To keep Ataraxia " +
                      "visible, open Taskbar Settings → Other system tray icons and turn it on.",
            CloseButtonText = "Got it",
            XamlRoot = xamlRoot,
        };
        _ = await dialog.ShowAsync();
        await settings.SaveAsync(SettingsViewModel.TraySettingsName, tray with { TrayHintShown = true });
    }

    private async Task HandleToastActivationAsync(string argument)
    {
        var arguments = ParseToastArguments(argument);
        if (!arguments.TryGetValue("runId", out var runId) || string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        if (arguments.TryGetValue("action", out var action) && action is "approve" or "deny")
        {
            try
            {
                arguments.TryGetValue("requestId", out var requestId);
                await Services.GetRequiredService<IGatewayClient>().ResolveApprovalAsync(
                    runId,
                    action == "approve" ? "once" : "deny",
                    string.IsNullOrWhiteSpace(requestId) ? null : requestId);
            }
            catch (Exception)
            {
                // The run's detail pane still shows the pending approval.
            }
        }

        _window?.DispatcherQueue.TryEnqueue(() => MainWindow.NavigateToMissionControl(runId));
    }

    private static Dictionary<string, string> ParseToastArguments(string argument)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in argument.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2)
            {
                result[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
            }
        }

        return result;
    }

    private static async Task InitializeGatewayClientAsync(IServiceProvider services)
    {
        var settings = services.GetRequiredService<ISettingsService>();
        var secrets = services.GetRequiredService<ISecretStore>();
        var client = (GatewayClient)services.GetRequiredService<IGatewayClient>();

        var saved = await settings.LoadAsync<ConnectionSettings>(SettingsViewModel.SettingsName);
        if (saved is not null && ConnectionValidation.IsValidBaseUrl(saved.BaseUrl))
        {
            client.SetBaseUrl(new Uri(saved.BaseUrl.Trim()));
        }

        client.SetApiKey(await secrets.GetAsync(SettingsViewModel.ApiKeySecretName));
    }
}
