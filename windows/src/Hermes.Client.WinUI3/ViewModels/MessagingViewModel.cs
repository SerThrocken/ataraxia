using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Messaging platforms. The gateway (0.21.6, source read 2026-10-11) exposes
/// no messaging-platform config routes — platforms are configured gateway-side
/// in config.yaml, and the only platform route is
/// POST /api/platforms/{platform}/events (an inbound event callback, not
/// config). So config editing is honestly unavailable here: this page shows
/// the live read-only platform status from GET /health/detailed.
/// </summary>
public sealed partial class MessagingViewModel : ObservableObject
{
    private readonly IGatewayClient _client;

    public MessagingViewModel(IGatewayClient client)
    {
        _client = client;
    }

    public string Notice =>
        "Messaging platforms are configured on the gateway (config.yaml), not "
        + "from this client: the gateway exposes no messaging-platform config "
        + "API (0.21.6, verified 2026-10-11). Config editing is unavailable "
        + "here; live platform status below is read-only.";

    public string Detail =>
        "Platform connections are read from GET /health/detailed. The only "
        + "platform route on the gateway is POST /api/platforms/{platform}/events, "
        + "an inbound event callback — not a configuration endpoint.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool HasError => Error is not null;

    public ObservableCollection<PlatformStatusRow> PlatformRows { get; } = new();

    public string Headline => PlatformRows.Count == 0
        ? "No platform data yet."
        : $"{PlatformRows.Count} platform{(PlatformRows.Count == 1 ? "" : "s")} reported by the gateway";

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
            PlatformRows.Clear();
            foreach (var row in PlatformStatusSummary.Build(health))
            {
                PlatformRows.Add(row);
            }

            OnPropertyChanged(nameof(Headline));
        }
        catch (Exception ex)
        {
            Error = $"Couldn't load platform status: {Trim(ex.Message)}";
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
