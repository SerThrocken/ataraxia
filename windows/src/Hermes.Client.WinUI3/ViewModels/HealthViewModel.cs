using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

public sealed record HealthCheckRow(string Name, string Status, string Detail);

/// <summary>
/// Gateway health dashboard: GET /health/detailed — readiness checks,
/// disk headroom, and the platform connection table. CPU, memory, uptime,
/// and restart history are NOT exposed by the gateway (verified 2026-10-11),
/// so this page shows only what the endpoint returns.
/// </summary>
public sealed partial class HealthViewModel : ObservableObject
{
    private readonly IGatewayClient _client;

    public HealthViewModel(IGatewayClient client)
    {
        _client = client;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool HasError => Error is not null;

    public DetailedHealth? Health { get; private set; }

    public ObservableCollection<HealthCheckRow> ReadinessRows { get; } = new();

    public ObservableCollection<KeyValuePair<string, PlatformStatus>> PlatformRows { get; } = new();

    public string Headline => Health is null
        ? "No health data yet."
        : $"Gateway {Health.Version} · {Health.Status}";

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
            Health = await _client.GetDetailedHealthAsync();
            BuildRows(Health);
            OnPropertyChanged(nameof(Health));
            OnPropertyChanged(nameof(Headline));
        }
        catch (Exception ex)
        {
            Error = $"Couldn't load gateway health: {Trim(ex.Message)}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildRows(DetailedHealth health)
    {
        ReadinessRows.Clear();
        var checks = health.Readiness?.Checks;
        if (checks is null)
        {
            ReadinessRows.Add(new HealthCheckRow("readiness", "unknown", "Readiness block absent."));
        }
        else
        {
            AddRow("state_db", checks.StateDb?.Status, "State database");
            AddRow("session_store", checks.SessionStore?.Status, "Session store");
            AddRow("config", checks.Config?.Status, "Configuration");
            AddRow("model", checks.Model?.Status, "Model runtime");
            AddRow("disk", checks.Disk?.Status,
                checks.Disk is null ? "—"
                    : $"{checks.Disk.UsedPercent:F1}% used, {FormatBytes(checks.Disk.FreeBytes)} free");
            AddRow("gateway", checks.Gateway?.Status,
                checks.Gateway is null ? "—"
                    : $"{checks.Gateway.ConnectedPlatforms}/{checks.Gateway.PlatformCount} platforms connected");
            AddRow("queues", checks.BackgroundQueues?.Status,
                checks.BackgroundQueues is null ? "—"
                    : $"{checks.BackgroundQueues.ActiveApiRuns} active runs");
        }

        PlatformRows.Clear();
        foreach (var kv in health.Platforms.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            PlatformRows.Add(kv);
        }
    }

    private void AddRow(string name, string? status, string detail) =>
        ReadinessRows.Add(new HealthCheckRow(name, status ?? "unknown", detail));

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            >= 1L << 40 => $"{bytes / (double)(1L << 40):F1} TiB",
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GiB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MiB",
            _ => $"{bytes} B",
        };

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
