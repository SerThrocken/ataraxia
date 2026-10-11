using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>One row of the provider board, from /health/detailed's platform table.</summary>
public sealed record ProviderRow(
    string Name,
    string State,
    bool NeedsAttention,
    string ErrorText,
    string LatencyText,
    string UpdatedText);

/// <summary>
/// Provider health board: connection state, last error, and writer
/// bookkeeping per platform from GET /health/detailed. Per-provider
/// latency, error rates, and quota state are NOT exposed by the gateway
/// (verified 2026-10-11) — the board shows what the endpoint returns and
/// says so, rather than inventing numbers.
/// </summary>
public sealed partial class ProviderHealthViewModel : ObservableObject
{
    private readonly IGatewayClient _client;

    public ProviderHealthViewModel(IGatewayClient client)
    {
        _client = client;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool HasError => Error is not null;

    public ObservableCollection<ProviderRow> Providers { get; } = new();

    public string Headline => Providers.Count == 0
        ? "No provider data yet."
        : $"{Providers.Count(p => !p.NeedsAttention)} of {Providers.Count} providers healthy";

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
            Providers.Clear();
            foreach (var kv in health.Platforms.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                Providers.Add(ToRow(kv.Key, kv.Value));
            }

            OnPropertyChanged(nameof(Headline));
        }
        catch (Exception ex)
        {
            Error = $"Couldn't load provider health: {Trim(ex.Message)}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static ProviderRow ToRow(string name, PlatformStatus status)
    {
        var error = string.IsNullOrWhiteSpace(status.ErrorMessage)
            ? (status.ErrorCode ?? "—")
            : $"{status.ErrorCode ?? "error"}: {status.ErrorMessage}";

        var latency = status.Metrics?.MetricsToday is { } today
            ? $"p95 {today.LatencyP95Ms:F0} ms today"
            : "—";

        var updated = status.UpdatedAt is { } at
            ? RelativeTime.Format(at)
            : "unknown";

        return new ProviderRow(name, status.State, status.NeedsAttention, error, latency, updated);
    }

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
