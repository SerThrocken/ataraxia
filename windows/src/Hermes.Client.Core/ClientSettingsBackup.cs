using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// One settings backup file: the raw connection-settings JSON blob
/// verbatim plus the DPAPI API key. The API key is stored IN PLAINTEXT
/// in the file — the UI warns before writing one.
/// </summary>
public sealed record ClientSettingsBackupFile(
    int FormatVersion,
    DateTimeOffset ExportedAtUtc,
    string? SettingsJson,
    string? ApiKey);

/// <summary>What <see cref="ClientSettingsBackup.ImportAsync"/> restored.</summary>
public sealed record ClientSettingsBackupSummary(
    bool RestoredSettings,
    bool RestoredApiKey,
    DateTimeOffset ExportedAtUtc);

/// <summary>
/// Client-side settings backup. The gateway exposes no backup or restore
/// API (verified 2026-10-11 against gateway 0.21.6), so this covers only
/// what lives on this PC: the connection-settings blob and the API key.
/// Secrets stay in DPAPI until the user explicitly exports them.
/// </summary>
public sealed class ClientSettingsBackup(
    ISettingsService settings,
    ISecretStore secrets,
    string settingsName,
    string secretName)
{
    /// <summary>Backup file format version this client reads and writes.</summary>
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes the versioned backup file: settings blob JSON verbatim,
    /// API key in plaintext, export timestamp. Overwrites the destination
    /// silently — the page confirms with the user first.
    /// </summary>
    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var settingsJson = await settings.LoadRawAsync(settingsName, cancellationToken).ConfigureAwait(false);
        var apiKey = await secrets.GetAsync(secretName, cancellationToken).ConfigureAwait(false);
        var file = new ClientSettingsBackupFile(FormatVersion, DateTimeOffset.UtcNow, settingsJson, apiKey);
        var json = JsonSerializer.Serialize(file, Json);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a backup file, validates it, and restores the settings blob
    /// and (when the file carries one) the API key. A file without an API
    /// key never clears the stored key — it only restores settings.
    /// </summary>
    /// <exception cref="FileNotFoundException">No file at <paramref name="filePath"/>.</exception>
    /// <exception cref="InvalidDataException">
    /// Not an Ataraxia settings backup, an unsupported format version, or
    /// corrupt settings JSON inside the file.
    /// </exception>
    public async Task<ClientSettingsBackupSummary> ImportAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        ClientSettingsBackupFile? file;
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            file = JsonSerializer.Deserialize<ClientSettingsBackupFile>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Not an Ataraxia settings backup: {filePath}", ex);
        }

        if (file is null)
        {
            throw new InvalidDataException($"Not an Ataraxia settings backup: {filePath}");
        }

        if (file.FormatVersion != FormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported backup format version {file.FormatVersion}; " +
                $"this client reads version {FormatVersion}.");
        }

        var restoredSettings = false;
        if (!string.IsNullOrWhiteSpace(file.SettingsJson))
        {
            try
            {
                using var _ = JsonDocument.Parse(file.SettingsJson);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Backup file contains corrupt settings JSON.", ex);
            }

            await settings.SaveRawAsync(settingsName, file.SettingsJson, cancellationToken).ConfigureAwait(false);
            restoredSettings = true;
        }

        var restoredApiKey = false;
        if (!string.IsNullOrEmpty(file.ApiKey))
        {
            await secrets.SetAsync(secretName, file.ApiKey, cancellationToken).ConfigureAwait(false);
            restoredApiKey = true;
        }

        return new ClientSettingsBackupSummary(restoredSettings, restoredApiKey, file.ExportedAtUtc);
    }
}
