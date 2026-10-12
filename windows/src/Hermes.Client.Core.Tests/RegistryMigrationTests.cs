using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// The pre-registry single connection (settings blob "connection" + DPAPI
/// secret "gateway-api-key") must become the first registry entry — imported,
/// never orphaned.
/// </summary>
public sealed class RegistryMigrationTests
{
    private const string LegacySettingsName = "connection";
    private const string LegacySecretName = "gateway-api-key";

    private readonly FakeSettingsService _settings = new();
    private readonly FakeSecretStore _secrets = new();
    private GatewayConnectionRegistry Registry => new(_settings, _secrets);

    private async Task SeedLegacyAsync(string baseUrl, string? apiKey)
    {
        await _settings.SaveAsync(LegacySettingsName,
            new LegacyConnectionSettings { BaseUrl = baseUrl });
        if (apiKey is not null)
        {
            await _secrets.SetAsync(LegacySecretName, apiKey);
        }
    }

    [Fact]
    public async Task MigratesLegacyConnection_AsFirstEntry()
    {
        await SeedLegacyAsync("http://100.101.250.118:8642", "legacy-key-1234567890");

        var migrated = await Registry.EnsureMigratedFromSingleConnectionAsync(
            LegacySettingsName, LegacySecretName);

        Assert.True(migrated);
        var list = await Registry.ListAsync();
        var entry = Assert.Single(list);
        Assert.Equal("imported", entry.Id);
        Assert.Equal("100.101.250.118", entry.Host);
        Assert.Equal(8642, entry.Port);
        Assert.False(entry.UseTls);
        Assert.Equal(entry.Id, (await Registry.GetActiveAsync())!.Id);

        // The key moved: present under the new alias, gone from the old one.
        Assert.Equal("legacy-key-1234567890", await Registry.GetApiKeyAsync(entry.Id));
        Assert.False(_secrets.HasSecret(LegacySecretName));
    }

    [Fact]
    public async Task Migration_ParsesTlsAndPort()
    {
        await SeedLegacyAsync("https://gateway.example.com:9999", null);

        await Registry.EnsureMigratedFromSingleConnectionAsync(
            LegacySettingsName, LegacySecretName);

        var entry = Assert.Single(await Registry.ListAsync());
        Assert.Equal("gateway.example.com", entry.Host);
        Assert.Equal(9999, entry.Port);
        Assert.True(entry.UseTls);
        Assert.Equal("https://gateway.example.com:9999/", entry.BaseUrl.ToString());
    }

    [Fact]
    public async Task Migration_IsIdempotent()
    {
        await SeedLegacyAsync("http://100.101.250.118:8642", "legacy-key-1234567890");
        var registry = Registry;

        Assert.True(await registry.EnsureMigratedFromSingleConnectionAsync(
            LegacySettingsName, LegacySecretName));
        Assert.False(await registry.EnsureMigratedFromSingleConnectionAsync(
            LegacySettingsName, LegacySecretName));

        Assert.Single(await registry.ListAsync());
    }

    [Fact]
    public async Task Migration_DoesNothing_WhenNoLegacyBlob()
    {
        var migrated = await Registry.EnsureMigratedFromSingleConnectionAsync(
            LegacySettingsName, LegacySecretName);

        Assert.False(migrated);
        Assert.Empty(await Registry.ListAsync());
        Assert.Null(await Registry.GetActiveAsync());
    }

    [Fact]
    public async Task Migration_DoesNothing_WhenLegacyUrlInvalid()
    {
        await SeedLegacyAsync("not-a-url", "legacy-key-1234567890");

        var migrated = await Registry.EnsureMigratedFromSingleConnectionAsync(
            LegacySettingsName, LegacySecretName);

        Assert.False(migrated);
        Assert.Empty(await Registry.ListAsync());
        // The key is left alone: nothing valid to attach it to.
        Assert.True(_secrets.HasSecret(LegacySecretName));
    }

    [Fact]
    public async Task Migration_KeepsExistingActive_WhenRegistryAlreadyPopulated()
    {
        var registry = Registry;
        var existing = await registry.AddAsync(
            new GatewayConnection { Name = "Mine", Host = "example.com" });
        await SeedLegacyAsync("http://100.101.250.118:8642", "legacy-key-1234567890");

        Assert.True(await registry.EnsureMigratedFromSingleConnectionAsync(
            LegacySettingsName, LegacySecretName));

        Assert.Equal(2, (await registry.ListAsync()).Count);
        Assert.Equal(existing, (await registry.GetActiveAsync())!.Id);
    }
}
