using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Hermes.Client.Infrastructure;
using Hermes.Client.WinUI3.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// URL-only settings blob. The API key is never stored here — it lives
/// in <see cref="ISecretStore"/> (DPAPI) under <c>gateway-api-key</c>.
/// </summary>
public sealed record ConnectionSettings
{
    public string BaseUrl { get; init; } = SettingsViewModel.DefaultBaseUrl;
}

/// <summary>
/// Gateway connection: Tailscale-first base URL, DPAPI-backed API key,
/// connectivity test (GET /health, then GET /v1/capabilities), and the
/// app-wide <see cref="ConnectionState"/>. Shared by the first-run
/// ConnectPage and the Settings page.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string DefaultBaseUrl = "http://100.101.250.118:8642";
    public const string LoopbackBaseUrl = "http://127.0.0.1:8642";

    /// <summary>Public so the app bootstrap can materialize the shared gateway client.</summary>
    public const string ApiKeySecretName = "gateway-api-key";

    /// <summary>Public so the app bootstrap can materialize the shared gateway client.</summary>
    public const string SettingsName = "connection";

    /// <summary>Settings-blob name for the tray + global-hotkey settings.</summary>
    public const string TraySettingsName = "tray";

    /// <summary>Settings-blob name for the companion-HUD settings (Phase 4).</summary>
    public const string HudSettingsName = "hud";

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly ConnectionTester _tester;
    private readonly IConnectionState _connectionState;
    private readonly IHotkeyRegistrar _hotkeys;

    [ObservableProperty]
    public partial string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>Key entry only — cleared from memory right after saving.</summary>
    [ObservableProperty]
    public partial string ApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasStoredKey { get; set; }

    [ObservableProperty]
    public partial string KeyStatusText { get; set; } = "No API key saved yet.";

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    public partial string TestResult { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CapabilitySummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConnectionStateText { get; set; } = "Not tested yet";

    /// <summary>Global quick-entry hotkey, e.g. "Ctrl+Shift+A".</summary>
    [ObservableProperty]
    public partial string HotkeyText { get; set; } = "Ctrl+Shift+A";

    /// <summary>Status line under the hotkey box; carries conflict errors.</summary>
    [ObservableProperty]
    public partial string HotkeyStatus { get; set; } = string.Empty;

    /// <summary>Companion-HUD master switch (opt-in; the overlay starts hidden).</summary>
    [ObservableProperty]
    public partial bool HudEnabled { get; set; }

    /// <summary>Companion-HUD opacity, 35–100.</summary>
    [ObservableProperty]
    public partial double HudOpacityPercent { get; set; } = HudSettings.DefaultOpacity * 100;

    /// <summary>Companion-HUD click-through (mouse clicks pass through).</summary>
    [ObservableProperty]
    public partial bool HudClickThrough { get; set; }

    /// <summary>Companion-HUD global toggle hotkey, e.g. "Ctrl+Shift+H".</summary>
    [ObservableProperty]
    public partial string HudHotkeyText { get; set; } = string.Empty;

    /// <summary>Status line under the HUD section.</summary>
    [ObservableProperty]
    public partial string HudStatus { get; set; } = string.Empty;

    private bool _loadingHud;

    public event EventHandler? ContinueRequested;

    public SettingsViewModel(
        ISettingsService settings,
        ISecretStore secrets,
        ConnectionTester tester,
        IConnectionState connectionState,
        IHotkeyRegistrar hotkeys)
    {
        _settings = settings;
        _secrets = secrets;
        _tester = tester;
        _connectionState = connectionState;
        _hotkeys = hotkeys;
        _connectionState.StateChanged += (_, state) => ConnectionStateText = state switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Disconnected => "Disconnected",
            _ => "Not tested yet",
        };
    }

    public async Task InitializeAsync()
    {
        var saved = await _settings.LoadAsync<ConnectionSettings>(SettingsName);
        if (saved is not null && ConnectionValidation.IsValidBaseUrl(saved.BaseUrl))
        {
            BaseUrl = saved.BaseUrl;
        }

        HasStoredKey = await _secrets.GetAsync(ApiKeySecretName) is not null;
        KeyStatusText = HasStoredKey
            ? "API key saved (DPAPI-encrypted)."
            : "No API key saved yet.";

        await InitializeTrayAsync();
        await InitializeHudAsync();
    }

    /// <summary>
    /// Loads the saved hotkey and registers it. A conflict is reported in
    /// <see cref="HotkeyStatus"/> — never a crash, never silent.
    /// </summary>
    private async Task InitializeTrayAsync()
    {
        var saved = await _settings.LoadAsync<TraySettings>(TraySettingsName);
        if (saved is not null && HotkeyBinding.TryParse(saved.Hotkey, out var parsed) && parsed is not null)
        {
            HotkeyText = parsed.ToString();
        }

        if (HotkeyBinding.TryParse(HotkeyText, out var binding) && binding is not null)
        {
            if (!_hotkeys.TryRegister(binding, out var error))
            {
                HotkeyStatus = error ?? "Couldn't register the global hotkey.";
            }
        }
    }

    [RelayCommand]
    private async Task ApplyHotkeyAsync()
    {
        if (!HotkeyBinding.TryParse(HotkeyText, out var binding) || binding is null)
        {
            HotkeyStatus = "That combo didn't parse — try something like Ctrl+Shift+A.";
            return;
        }

        if (!_hotkeys.TryRegister(binding, out var error))
        {
            HotkeyStatus = error ?? "Couldn't register that hotkey.";
            return;
        }

        HotkeyText = binding.ToString();
        var saved = await _settings.LoadAsync<TraySettings>(TraySettingsName) ?? new TraySettings();
        await _settings.SaveAsync(TraySettingsName, saved with { Hotkey = binding.ToString() });
        HotkeyStatus = $"Global hotkey set to {binding}.";
    }

    /// <summary>
    /// Loads the companion-HUD settings and registers the saved toggle
    /// hotkey. A conflict is reported in <see cref="HudStatus"/> — never a
    /// crash, never silent.
    /// </summary>
    private async Task InitializeHudAsync()
    {
        _loadingHud = true;
        try
        {
            var saved = await _settings.LoadAsync<HudSettings>(HudSettingsName);
            if (saved is null)
            {
                return;
            }

            var normalized = saved.Normalized();
            HudEnabled = normalized.Enabled;
            HudOpacityPercent = normalized.Opacity * 100;
            HudClickThrough = normalized.ClickThrough;
            HudHotkeyText = normalized.Hotkey;
            RegisterHudHotkey(normalized.Hotkey);
        }
        finally
        {
            _loadingHud = false;
        }
    }

    private bool RegisterHudHotkey(string hotkeyText)
    {
        if (string.IsNullOrWhiteSpace(hotkeyText))
        {
            return true;
        }

        if (_hotkeys is not GlobalHotkeyService registrar)
        {
            return true;
        }

        if (!HotkeyBinding.TryParse(hotkeyText, out var binding) || binding is null)
        {
            HudStatus = $"Saved HUD hotkey \"{hotkeyText}\" didn't parse — set it again below.";
            return false;
        }

        if (!registrar.TryRegisterHud(binding, out var error))
        {
            HudStatus = error ?? "Couldn't register the HUD hotkey.";
            return false;
        }

        return true;
    }

    partial void OnHudEnabledChanged(bool value)
    {
        if (_loadingHud)
        {
            return;
        }

        _ = ApplyHudEnabledAsync(value);
    }

    private async Task ApplyHudEnabledAsync(bool value)
    {
        var hud = App.Services.GetService<HudService>();
        if (hud is null)
        {
            HudStatus = "HUD service isn't available.";
            return;
        }

        await hud.SetEnabledAsync(value);
        HudStatus = value ? "Companion HUD shown." : "Companion HUD hidden.";
    }

    /// <summary>
    /// Saves opacity, click-through, and the toggle hotkey; appearance
    /// applies live to a visible overlay.
    /// </summary>
    [RelayCommand]
    private async Task ApplyHudAsync()
    {
        var hud = App.Services.GetService<HudService>();
        if (hud is null)
        {
            HudStatus = "HUD service isn't available.";
            return;
        }

        var opacity = HudSettings.ClampOpacity(HudOpacityPercent / 100.0);
        HudOpacityPercent = opacity * 100;

        await hud.ApplyAppearanceAsync(opacity, HudClickThrough);
        await hud.SetHotkeyAsync(HudHotkeyText);
        if (RegisterHudHotkey(HudHotkeyText))
        {
            HudStatus = "HUD settings saved.";
        }
    }

    [RelayCommand]
    private void UseLoopback() => BaseUrl = LoopbackBaseUrl;

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!ConnectionValidation.IsValidBaseUrl(BaseUrl))
        {
            TestResult = "Enter a valid http(s) gateway URL.";
            return;
        }

        // Prefer a freshly typed key; fall back to the stored one.
        var key = string.IsNullOrWhiteSpace(ApiKey)
            ? await _secrets.GetAsync(ApiKeySecretName)
            : ApiKey.Trim();

        if (!ConnectionValidation.IsValidApiKey(key))
        {
            TestResult = "API key must be at least 16 characters.";
            _connectionState.SetState(ConnectionState.Disconnected);
            return;
        }

        IsTesting = true;
        TestResult = "Testing…";
        CapabilitySummary = string.Empty;
        try
        {
            var result = await _tester.TestAsync(new Uri(BaseUrl.Trim()), key);
            if (result.Succeeded && result is { Health: not null, Capabilities: not null })
            {
                _connectionState.SetState(ConnectionState.Connected);
                TestResult = $"Connected — gateway {result.Health.Version} ({result.Health.Platform}).";
                CapabilitySummary =
                    $"{result.Capabilities.EnabledFeatures.Count} features enabled · " +
                    $"{result.Capabilities.EndpointCount} endpoints · " +
                    string.Join(", ", result.Capabilities.EnabledFeatures.Take(6));
            }
            else
            {
                _connectionState.SetState(ConnectionState.Disconnected);
                TestResult = result.Message;
            }
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!ConnectionValidation.IsValidBaseUrl(BaseUrl))
        {
            Status = "Enter a valid http(s) gateway URL.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            if (!ConnectionValidation.IsValidApiKey(ApiKey))
            {
                Status = "API key must be at least 16 characters.";
                return;
            }

            await _secrets.SetAsync(ApiKeySecretName, ApiKey.Trim());
            ApiKey = string.Empty; // never keep the key in memory longer than needed
            HasStoredKey = true;
            KeyStatusText = "API key saved (DPAPI-encrypted).";
        }

        await _settings.SaveAsync(SettingsName, new ConnectionSettings { BaseUrl = BaseUrl.Trim() });
        Status = HasStoredKey
            ? "Connection saved."
            : "URL saved. Add your API key to connect.";
    }

    /// <summary>
    /// First-run flow: save, then move on to the app only if a key is stored.
    /// </summary>
    [RelayCommand]
    private async Task SaveAndContinueAsync()
    {
        await SaveAsync();
        if (HasStoredKey)
        {
            ContinueRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
