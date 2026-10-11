using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

public sealed record DailySpendRow(DailySpend Spend, double BarWidth)
{
    public string DateLabel => Spend.Date == DateOnly.MinValue
        ? "date unknown"
        : Spend.Date.ToString("MMM d");
    public string SpendLabel => $"${Spend.CostUsd:F4} · {Spend.TotalTokens:N0} tokens";
}

public sealed record ProviderSpendRow(ProviderSpend Spend, double BarWidth)
{
    public string SpendLabel =>
        $"${Spend.CostUsd:F4} · {Spend.TotalTokens:N0} tokens · {Spend.SessionCount} sessions";
}

/// <summary>
/// Cost analytics: per-day / per-provider / per-session spend rolled up
/// client-side from the full session list (the gateway exposes no
/// server-side usage endpoint). Bars are hand-rolled rectangles scaled in
/// the view model — no chart library.
/// </summary>
public sealed partial class AnalyticsViewModel : ObservableObject
{
    private const double MaxBarWidth = 280;

    private readonly IGatewayClient _client;

    public AnalyticsViewModel(IGatewayClient client)
    {
        _client = client;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool HasError => Error is not null;

    public AnalyticsSummary? Summary { get; private set; }

    public ObservableCollection<DailySpendRow> DailyRows { get; } = new();

    public ObservableCollection<ProviderSpendRow> ProviderRows { get; } = new();

    public ObservableCollection<SessionSpend> TopSessions { get; } = new();

    public string Headline => Summary is null
        ? "No analytics yet."
        : $"{Summary.SessionCount} sessions · {Summary.TotalTokens:N0} tokens · ${Summary.TotalCostUsd:F4}";

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
            var sessions = await _client.GetAllSessionsAsync();
            Summary = UsageAnalytics.Summarize(sessions);
            BuildRows(Summary);
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Headline));
        }
        catch (Exception ex)
        {
            Error = $"Couldn't load analytics: {Trim(ex.Message)}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildRows(AnalyticsSummary summary)
    {
        DailyRows.Clear();
        var maxDay = summary.PerDay.Max(d => (double?)d.CostUsd) ?? 0;
        foreach (var day in summary.PerDay)
        {
            DailyRows.Add(new DailySpendRow(day, Scale(day.CostUsd, maxDay)));
        }

        ProviderRows.Clear();
        var maxProvider = summary.PerProvider.Max(p => (double?)p.CostUsd) ?? 0;
        foreach (var provider in summary.PerProvider)
        {
            ProviderRows.Add(new ProviderSpendRow(provider, Scale(provider.CostUsd, maxProvider)));
        }

        TopSessions.Clear();
        foreach (var session in summary.TopSessions)
        {
            TopSessions.Add(session);
        }
    }

    private static double Scale(double value, double max) =>
        max <= 0 ? 0 : Math.Max(2, value / max * MaxBarWidth);

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
