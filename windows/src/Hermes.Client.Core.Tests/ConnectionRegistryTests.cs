using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class ConnectionRegistryTests
{
    private readonly FakeSettingsService _settings = new();
    private readonly FakeSecretStore _secrets = new();
    private GatewayConnectionRegistry Registry => new(_settings, _secrets);

    private static GatewayConnection Conn(string name = "Acer", string host = "100.101.250.118") =>
        new() { Name = name, Host = host };

    [Fact]
    public async Task AddAsync_ReturnsId_AndListsConnection()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Conn());

        Assert.False(string.IsNullOrWhiteSpace(id));
        var list = await registry.ListAsync();
        var stored = Assert.Single(list);
        Assert.Equal(id, stored.Id);
        Assert.Equal("Acer", stored.Name);
        Assert.Equal("100.101.250.118", stored.Host);
        Assert.Equal(8642, stored.Port);
        Assert.Equal("http://100.101.250.118:8642/", stored.BaseUrl.ToString());
    }

    [Fact]
    public async Task AddAsync_FirstConnectionBecomesActive()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Conn());

        var active = await registry.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(id, active.Id);
    }

    [Fact]
    public async Task AddAsync_SecondConnection_DoesNotStealActive()
    {
        var registry = Registry;
        var first = await registry.AddAsync(Conn("Acer"));
        await registry.AddAsync(Conn("Cloud", "example.com"));

        Assert.Equal(first, (await registry.GetActiveAsync())!.Id);
    }

    [Fact]
    public async Task AddAsync_RejectsDuplicateId()
    {
        var registry = Registry;
        var first = Conn(); first = first with { Id = "dup" };
        await registry.AddAsync(first);

        var second = Conn("Other"); second = second with { Id = "dup" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.AddAsync(second));
    }

    [Theory]
    [InlineData("", "example.com", 8642)]
    [InlineData("Acer", "not a host!", 8642)]
    [InlineData("Acer", "", 8642)]
    [InlineData("Acer", "example.com", 0)]
    [InlineData("Acer", "example.com", 70000)]
    public async Task AddAsync_RejectsInvalid(string name, string host, int port)
    {
        var registry = Registry;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            registry.AddAsync(new GatewayConnection { Name = name, Host = host, Port = port }));
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForUnknownId()
    {
        Assert.Null(await Registry.GetAsync("nope"));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesConnection()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Conn());

        var updated = new GatewayConnection
            { Id = id, Name = "Acer 2", Host = "example.com", Port = 9999, UseTls = true };
        await registry.UpdateAsync(updated);

        var stored = await registry.GetAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("Acer 2", stored.Name);
        Assert.Equal("example.com", stored.Host);
        Assert.Equal(9999, stored.Port);
        Assert.True(stored.UseTls);
        Assert.Equal("https://example.com:9999/", stored.BaseUrl.ToString());
    }

    [Fact]
    public async Task UpdateAsync_Throws_ForUnknownId()
    {
        var missing = new GatewayConnection { Id = "nope", Name = "X", Host = "example.com" };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Registry.UpdateAsync(missing));
    }

    [Fact]
    public async Task SetActiveIdAsync_Throws_ForUnknownId()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Registry.SetActiveIdAsync("nope"));
    }

    [Fact]
    public async Task ApiKey_RoundTrips_ThroughPerConnectionAlias()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Conn());

        await registry.SetApiKeyAsync(id, "secret-key-1234567890");
        Assert.Equal("secret-key-1234567890", await registry.GetApiKeyAsync(id));
        Assert.True(_secrets.HasSecret($"gateway-api-key:{id}"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesConnection_AndItsSecret()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Conn());
        await registry.SetApiKeyAsync(id, "secret-key-1234567890");

        await registry.DeleteAsync(id);

        Assert.Empty(await registry.ListAsync());
        Assert.False(_secrets.HasSecret($"gateway-api-key:{id}"));
    }

    [Fact]
    public async Task DeleteAsync_FallsBackActive_ToFirstRemaining()
    {
        var registry = Registry;
        var first = await registry.AddAsync(Conn("Acer"));
        var second = await registry.AddAsync(Conn("Cloud", "example.com"));

        await registry.DeleteAsync(first);

        Assert.Equal(second, (await registry.GetActiveAsync())!.Id);
    }

    [Fact]
    public async Task DeleteAsync_Throws_ForUnknownId()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Registry.DeleteAsync("nope"));
    }

    [Fact]
    public async Task RegistryBlob_ContainsNoSecrets()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Conn());
        await registry.SetApiKeyAsync(id, "super-secret-api-key-1234567890");

        var raw = _settings.RawBlob(GatewayConnectionRegistry.SettingsName);
        Assert.NotNull(raw);
        Assert.DoesNotContain("super-secret-api-key-1234567890", raw);
    }
}
