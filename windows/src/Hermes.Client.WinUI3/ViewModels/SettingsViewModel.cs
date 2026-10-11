using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Hermes.Client.Infrastructure;

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

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly ConnectionTester _tester;
    private readonly IConnectionState _connectionState;

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

    public event EventHandler? ContinueRequested;

    public SettingsViewModel(
        ISettingsService settings,
        ISecretStore secrets,
        ConnectionTester tester,
        IConnectionState connectionState)
    {
        _settings = settings;
        _secrets = secrets;
        _tester = tester;
        _connectionState = connectionState;
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
