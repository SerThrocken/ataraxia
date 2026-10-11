using System.Text.Json;
using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Tests for <see cref="ClientSettingsBackup"/> — the client-side
/// settings export/import. The gateway exposes no backup or restore API
/// (verified 2026-10-11, gateway 0.21.6), so this covers only what lives
/// on the PC: the connection-settings blob and the API key.
/// </summary>
public sealed class ClientSettingsBackupTests
{
    private const string SettingsName = "connection";
    private const string SecretName = "gateway-api-key";

    private sealed class FakeSettings : ISettingsService
    {
        public readonly Dictionary<string, string> Blobs = new();

        public Task<T?> LoadAsync<T>(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Blobs.TryGetValue(name, out var json)
                ? JsonSerializer.Deserialize<T>(json)
                : default);

        public Task SaveAsync<T>(string name, T value, CancellationToken cancellationToken = default)
        {
            Blobs[name] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task<string?> LoadRawAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(Blobs.TryGetValue(name, out var json) ? json : null);

        public Task SaveRawAsync(string name, string json, CancellationToken cancellationToken = default)
        {
            Blobs[name] = json;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSecrets : ISecretStore
    {
        public readonly Dictionary<string, string> Secrets = new();

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Secrets.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Secrets[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Secrets.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static (ClientSettingsBackup Backup, FakeSettings Settings, FakeSecrets Secrets) Create()
    {
        var settings = new FakeSettings();
        var secrets = new FakeSecrets();
        return (new ClientSettingsBackup(settings, secrets, SettingsName, SecretName), settings, secrets);
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"ataraxia-backup-test-{Guid.NewGuid():N}.json");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static ClientSettingsBackupFile? ReadBackupFile(string path) =>
        JsonSerializer.Deserialize<ClientSettingsBackupFile>(File.ReadAllText(path), Json);

    [Fact]
    public async Task Export_WritesVersionedFileWithSettingsAndKey()
    {
        var (backup, settings, secrets) = Create();
        await settings.SaveRawAsync(SettingsName, "{\"BaseUrl\":\"http://example:8642\"}");
        await secrets.SetAsync(SecretName, "key-123");
        var path = TempPath();
        try
        {
            await backup.ExportAsync(path);

            var file = ReadBackupFile(path);
            Assert.NotNull(file);
            Assert.Equal(ClientSettingsBackup.FormatVersion, file.FormatVersion);
            Assert.Equal("{\"BaseUrl\":\"http://example:8642\"}", file.SettingsJson);
            Assert.Equal("key-123", file.ApiKey);
            Assert.True((DateTimeOffset.UtcNow - file.ExportedAtUtc).Duration() < TimeSpan.FromMinutes(1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Export_WithNothingStored_WritesValidFileWithNulls()
    {
        var (backup, _, _) = Create();
        var path = TempPath();
        try
        {
            await backup.ExportAsync(path);

            var file = ReadBackupFile(path);
            Assert.NotNull(file);
            Assert.Equal(ClientSettingsBackup.FormatVersion, file.FormatVersion);
            Assert.Null(file.SettingsJson);
            Assert.Null(file.ApiKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Import_RoundTripsSettingsAndKey()
    {
        var (exporter, exportSettings, exportSecrets) = Create();
        await exportSettings.SaveRawAsync(SettingsName, "{\"BaseUrl\":\"http://example:8642\"}");
        await exportSecrets.SetAsync(SecretName, "key-123");
        var path = TempPath();
        var (importer, importSettings, importSecrets) = Create();
        try
        {
            await exporter.ExportAsync(path);
            var summary = await importer.ImportAsync(path);

            Assert.True(summary.RestoredSettings);
            Assert.True(summary.RestoredApiKey);
            Assert.Equal("{\"BaseUrl\":\"http://example:8642\"}", await importSettings.LoadRawAsync(SettingsName));
            Assert.Equal("key-123", await importSecrets.GetAsync(SecretName));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Import_WithoutApiKey_LeavesStoredKeyUntouched()
    {
        var (backup, settings, secrets) = Create();
        await settings.SaveRawAsync(SettingsName, "{\"BaseUrl\":\"http://example:8642\"}");
        var path = TempPath();
        var (importer, importSettings, importSecrets) = Create();
        await importSecrets.SetAsync(SecretName, "existing-key");
        try
        {
            await backup.ExportAsync(path);
            // Simulate a backup taken before any key was stored.
            var file = ReadBackupFile(path);
            Assert.NotNull(file);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file with { ApiKey = null }));

            var summary = await importer.ImportAsync(path);

            Assert.True(summary.RestoredSettings);
            Assert.False(summary.RestoredApiKey);
            Assert.Equal("existing-key", await importSecrets.GetAsync(SecretName));
            Assert.Equal("{\"BaseUrl\":\"http://example:8642\"}", await importSettings.LoadRawAsync(SettingsName));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Import_MissingFile_ThrowsFileNotFound()
    {
        var (backup, _, _) = Create();
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => backup.ImportAsync(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json")));
    }

    [Fact]
    public async Task Import_UnknownFormatVersion_ThrowsInvalidData()
    {
        var (backup, _, _) = Create();
        var path = TempPath();
        try
        {
            var file = new ClientSettingsBackupFile(99, DateTimeOffset.UtcNow, "{}", "k");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file));

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => backup.ImportAsync(path));
            Assert.Contains("99", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Import_CorruptJson_ThrowsInvalidData()
    {
        var (backup, _, _) = Create();
        var path = TempPath();
        try
        {
            await File.WriteAllTextAsync(path, "this is not json{{{");

            await Assert.ThrowsAsync<InvalidDataException>(() => backup.ImportAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Import_CorruptSettingsJson_ThrowsInvalidData()
    {
        var (backup, _, _) = Create();
        var path = TempPath();
        try
        {
            var file = new ClientSettingsBackupFile(
                ClientSettingsBackup.FormatVersion, DateTimeOffset.UtcNow, "not json", "k");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file));

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => backup.ImportAsync(path));
            Assert.Contains("settings", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Import_EmptyFile_RestoresNothingButSucceeds()
    {
        var (backup, _, _) = Create();
        var path = TempPath();
        var (importer, importSettings, importSecrets) = Create();
        try
        {
            await backup.ExportAsync(path); // nothing stored: nulls

            var summary = await importer.ImportAsync(path);

            Assert.False(summary.RestoredSettings);
            Assert.False(summary.RestoredApiKey);
            Assert.Null(await importSettings.LoadRawAsync(SettingsName));
            Assert.Null(await importSecrets.GetAsync(SecretName));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
