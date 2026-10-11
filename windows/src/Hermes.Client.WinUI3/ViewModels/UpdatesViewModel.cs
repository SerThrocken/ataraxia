using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Update management. The gateway exposes no update-check or
/// update-trigger routes and publishes no version feed (verified on 0.21.6,
/// 2026-10-11), so this page shows only the live gateway version from
/// GET /health/detailed and labels the rest honestly unavailable.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly IGatewayClient _client;

    public UpdatesViewModel(IGatewayClient client)
    {
        _client = client;
    }

    public string Notice =>
        "Update checking and applying are not available: the gateway "
        + "exposes no update-check or update-trigger API and publishes no "
        + "version feed to compare against (0.21.6, verified 2026-10-11).";

    public string PendingMarkerNote =>
        "The Electron app's pending-update marker is a desktop-side "
        + "concept; this native client keeps no such marker. Updates are "
        + "applied on the gateway host, not from this app.";

    public string Detail =>
        "When a future gateway adds update APIs, the pending-update status "
        + "and the update trigger will appear here.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Headline))]
    public partial string? GatewayVersion { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool HasError => Error is not null;

    public string Headline => string.IsNullOrWhiteSpace(GatewayVersion)
        ? "Gateway version: unknown."
        : $"Gateway version: {GatewayVersion}";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        Error = null;
        try
        {
            var health = await _client.GetDetailedHealthAsync();
            GatewayVersion = string.IsNullOrWhiteSpace(health.Version) ? "unknown" : health.Version;
        }
        catch (Exception ex)
        {
            Error = $"Couldn't load the gateway version: {Trim(ex.Message)}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
