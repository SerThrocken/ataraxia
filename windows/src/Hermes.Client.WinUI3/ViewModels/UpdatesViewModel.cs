using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Update management. The top half is this client's own self-updates via
/// Velopack (<see cref="UpdateCoordinator"/>): check, download, apply and
/// restart. The bottom half still shows the live gateway version from
/// GET /health/detailed — the gateway exposes no update-check or
/// update-trigger routes and publishes no version feed (verified on 0.21.6,
/// 2026-10-11), so that part stays honestly read-only.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly IGatewayClient _client;

    public UpdatesViewModel(IGatewayClient client, UpdateCoordinator coordinator)
    {
        _client = client;
        Updates = coordinator;
        Updates.PropertyChanged += OnUpdatesPropertyChanged;
    }

    /// <summary>Self-update state machine (Velopack-backed).</summary>
    public UpdateCoordinator Updates { get; }

    public bool CanCheckUpdates => Updates.CanCheck;

    public bool CanDownloadUpdate => Updates.CanDownload;

    public bool CanApplyUpdate => Updates.CanApply;

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

    public string GatewayNote =>
        "The gateway itself can't be updated from here: it exposes no "
        + "update-check or update-trigger API and publishes no version feed "
        + "to compare against (0.21.6, verified 2026-10-11).";

    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    public async Task CheckForUpdatesAsync() => await Updates.CheckForUpdatesAsync();

    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    public async Task DownloadUpdateAsync() => await Updates.DownloadAsync();

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    public void ApplyUpdate() => Updates.ApplyAndRestart();

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

    private void OnUpdatesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UpdateCoordinator.CanCheck)
            or nameof(UpdateCoordinator.CanDownload)
            or nameof(UpdateCoordinator.CanApply))
        {
            CheckForUpdatesCommand.NotifyCanExecuteChanged();
            DownloadUpdateCommand.NotifyCanExecuteChanged();
            ApplyUpdateCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanCheckUpdates));
            OnPropertyChanged(nameof(CanDownloadUpdate));
            OnPropertyChanged(nameof(CanApplyUpdate));
        }
    }

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
