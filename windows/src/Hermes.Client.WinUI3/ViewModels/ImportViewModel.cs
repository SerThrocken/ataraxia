using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Infrastructure;
using System.Collections.ObjectModel;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// One Electron connection row on the import screen. Screen-reader text
/// carries the label, kind, URL, and key status — never the key itself.
/// </summary>
public sealed partial class ImportableConnection : ObservableObject
{
    private readonly Action<ImportableConnection> _onSelected;

    public ImportableConnection(
        ElectronConnectionPreview preview,
        Action<ImportableConnection> onSelected)
    {
        Preview = preview;
        _onSelected = onSelected;
    }

    public ElectronConnectionPreview Preview { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string DisplayName => $"{Preview.Label} ({Preview.Kind})";

    public string Detail => Preview.ResolvedBaseUrl
        ?? "No gateway URL stored for this connection — it will be skipped.";

    public string KeyDetail => Preview.KeyAvailable
        ? $"API key found ({Preview.KeyLength} characters, {Preview.KeySourceDescription}) — it will be stored encrypted."
        : "No API key found for this connection.";

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _onSelected(this);
        }
    }
}

/// <summary>
/// "Import from Hermes Desktop" screen: scans the Electron app's data
/// (read-only), lets the user pick one connection plus optional desktop
/// settings, and imports them — URL into settings, key into DPAPI.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly ElectronImporter _importer;
    private readonly SettingsViewModel _settings;

    /// <summary>Where the Back button returns: "connect" (first-run) or "settings".</summary>
    public string ReturnPage { get; private set; } = "settings";

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<ImportableConnection> Connections { get; set; } = new();

    [ObservableProperty]
    public partial bool HasConnections { get; set; }

    [ObservableProperty]
    public partial bool ImportSettings { get; set; } = true;

    [ObservableProperty]
    public partial string SettingsSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSettings { get; set; }

    [ObservableProperty]
    public partial bool CanImport { get; set; }

    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    public partial string Result { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ImportSucceeded { get; set; }

    public ImportViewModel(ElectronImporter importer, SettingsViewModel settings)
    {
        _importer = importer;
        _settings = settings;
    }

    public async Task LoadAsync(string returnPage)
    {
        ReturnPage = returnPage;
        IsScanning = true;
        Status = "Scanning the Hermes Desktop data…";
        Result = string.Empty;
        ImportSucceeded = false;
        try
        {
            var preview = await _importer.PreviewAsync().ConfigureAwait(true);
            Connections = new ObservableCollection<ImportableConnection>(
                preview.Connections.Select(p => new ImportableConnection(p, SelectOnly)));

            var primary = Connections.FirstOrDefault(c => c.Preview.Id == preview.PrimaryId)
                ?? Connections.FirstOrDefault();
            if (primary is not null)
            {
                primary.IsSelected = true;
            }

            HasConnections = Connections.Count > 0;
            CanImport = HasConnections;
            HasSettings = preview.SettingsFound.Count > 0;
            if (!HasSettings)
            {
                ImportSettings = false;
            }

            SettingsSummary = HasSettings
                ? string.Join(" · ", preview.SettingsFound)
                : "No importable desktop settings found.";
            Status = HasConnections
                ? $"Found {Connections.Count} connection{(Connections.Count == 1 ? "" : "s")}."
                : "The Hermes Desktop app has no saved connections on this machine.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void SelectOnly(ImportableConnection selected)
    {
        foreach (var connection in Connections)
        {
            if (!ReferenceEquals(connection, selected))
            {
                connection.IsSelected = false;
            }
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var selected = Connections.FirstOrDefault(c => c.IsSelected);
        IsImporting = true;
        Result = string.Empty;
        try
        {
            var result = await _importer.ImportAsync(
                new ElectronImportSelection(selected?.Preview.Id, ImportSettings && HasSettings))
                .ConfigureAwait(true);
            Result = result.Summary;
            ImportSucceeded = result.ConnectionImported || result.KeyImported || result.SettingsImported.Count > 0;
            if (ImportSucceeded)
            {
                // Refresh the shared connection state so the connect screen
                // (first-run) sees the imported URL and key immediately.
                await _settings.InitializeAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            IsImporting = false;
        }
    }
}
