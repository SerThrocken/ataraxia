using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Backup and restore. The gateway exposes no backup or restore routes
/// (verified on 0.21.6, 2026-10-11), so gateway-side backup is an honest
/// empty state; the real functionality here is client-side export/import
/// of this PC's connection settings and API key.
/// </summary>
public sealed partial class BackupViewModel : ObservableObject
{
    private readonly ClientSettingsBackup _backup;

    public BackupViewModel(ClientSettingsBackup backup)
    {
        _backup = backup;
    }

    public string Notice =>
        "Gateway backup and restore are not available: the gateway exposes "
        + "no backup or restore API (0.21.6, verified 2026-10-11).";

    public string GatewayDetail =>
        "When a future gateway adds a backup API, this page would back up "
        + "the gateway state directory — sessions, agent runs, skills, and "
        + "configuration — from the gateway host.";

    public string LocalDetail =>
        "What works today: export this client's own connection settings and "
        + "API key to a file, and import them back. The backup file contains "
        + "the API key in plaintext — store it somewhere safe.";

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Writes the settings backup to <paramref name="filePath"/>.</summary>
    public async Task ExportAsync(string filePath)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = string.Empty;
        try
        {
            await _backup.ExportAsync(filePath);
            Status = $"Exported client settings to {filePath}.";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {Trim(ex.Message)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Restores the settings backup at <paramref name="filePath"/>.</summary>
    public async Task ImportAsync(string filePath)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = string.Empty;
        try
        {
            var summary = await _backup.ImportAsync(filePath);
            var parts = new List<string>();
            if (summary.RestoredSettings)
            {
                parts.Add("connection settings");
            }

            if (summary.RestoredApiKey)
            {
                parts.Add("API key");
            }

            Status = parts.Count == 0
                ? "Import read the file but it contained nothing to restore."
                : $"Imported {string.Join(" and ", parts)} from the backup exported {summary.ExportedAtUtc:u}.";
        }
        catch (Exception ex)
        {
            Status = $"Import failed: {Trim(ex.Message)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
