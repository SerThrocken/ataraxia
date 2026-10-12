using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class SshHostRegistryTests
{
    private readonly FakeSettingsService _settings = new();
    private readonly FakeSecretStore _secrets = new();
    private SshHostRegistry Registry => new(_settings, _secrets);

    private static SshHost Host(string name = "Acer", string host = "100.101.250.118") =>
        new() { Name = name, Host = host, Username = "tlcro" };

    [Fact]
    public async Task AddAsync_ReturnsId_AndListsHost()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Host());

        Assert.False(string.IsNullOrWhiteSpace(id));
        var stored = Assert.Single(await registry.ListAsync());
        Assert.Equal(id, stored.Id);
        Assert.Equal("Acer", stored.Name);
        Assert.Equal("100.101.250.118", stored.Host);
        Assert.Equal(22, stored.Port);
        Assert.Equal("tlcro", stored.Username);
        Assert.Equal(SshAuthKind.PrivateKey, stored.AuthKind);
    }

    [Fact]
    public async Task AddAsync_RejectsDuplicateId()
    {
        var registry = Registry;
        await registry.AddAsync(Host() with { Id = "dup" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.AddAsync(Host("Other") with { Id = "dup" }));
    }

    [Theory]
    [InlineData("", "example.com", 22, "tlcro")]      // blank name
    [InlineData("Acer", "not a host!", 22, "tlcro")]  // bad host
    [InlineData("Acer", "example.com", 0, "tlcro")]    // bad port
    [InlineData("Acer", "example.com", 70000, "tlcro")] // bad port
    [InlineData("Acer", "example.com", 22, "")]        // blank username
    public async Task AddAsync_RejectsInvalid(string name, string host, int port, string username)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Registry.AddAsync(new SshHost
                { Name = name, Host = host, Port = port, Username = username }));
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForUnknownId()
    {
        Assert.Null(await Registry.GetAsync("nope"));
    }

    [Fact]
    public async Task UpdateAsync_ReplacesHost()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Host());

        await registry.UpdateAsync(new SshHost
        {
            Id = id, Name = "Acer 2", Host = "example.com", Port = 2222,
            Username = "admin", AuthKind = SshAuthKind.Password,
        });

        var stored = await registry.GetAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("Acer 2", stored.Name);
        Assert.Equal(2222, stored.Port);
        Assert.Equal("admin", stored.Username);
        Assert.Equal(SshAuthKind.Password, stored.AuthKind);
    }

    [Fact]
    public async Task UpdateAsync_Throws_ForUnknownId()
    {
        var missing = Host() with { Id = "nope" };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Registry.UpdateAsync(missing));
    }

    [Fact]
    public async Task PrivateKey_RoundTrips_ThroughAlias()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Host());
        const string key = "-----BEGIN OPENSSH PRIVATE KEY-----\nfake\n-----END OPENSSH PRIVATE KEY-----";

        await registry.SetPrivateKeyAsync(id, key);

        Assert.Equal(key, await registry.GetPrivateKeyAsync(id));
        Assert.True(_secrets.HasSecret($"ssh-private-key:{id}"));
    }

    [Fact]
    public async Task Password_RoundTrips_ThroughAlias()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Host() with { AuthKind = SshAuthKind.Password });

        await registry.SetPasswordAsync(id, "hunter2-hunter2-hunter2");

        Assert.Equal("hunter2-hunter2-hunter2", await registry.GetPasswordAsync(id));
    }

    [Fact]
    public async Task KeyPassphrase_RoundTrips_ThroughAlias()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Host());

        await registry.SetKeyPassphraseAsync(id, "correct horse battery staple");

        Assert.Equal("correct horse battery staple",
            await registry.GetKeyPassphraseAsync(id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesHost_AndAllSecrets()
    {
        var registry = Registry;
        var id = await registry.AddAsync(Host());
        await registry.SetPrivateKeyAsync(id, "key");
        await registry.SetKeyPassphraseAsync(id, "phrase");
        await registry.SetPasswordAsync(id, "password");

        await registry.DeleteAsync(id);

        Assert.Empty(await registry.ListAsync());
        Assert.False(_secrets.HasSecret($"ssh-private-key:{id}"));
        Assert.False(_secrets.HasSecret($"ssh-key-passphrase:{id}"));
        Assert.False(_secrets.HasSecret($"ssh-password:{id}"));
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
        var id = await registry.AddAsync(Host());
        await registry.SetPrivateKeyAsync(id, "PRIVATE-KEY-MATERIAL-12345");
        await registry.SetPasswordAsync(id, "PASSWORD-MATERIAL-67890");

        var raw = _settings.RawBlob(SshHostRegistry.SettingsName);
        Assert.NotNull(raw);
        Assert.DoesNotContain("PRIVATE-KEY-MATERIAL-12345", raw);
        Assert.DoesNotContain("PASSWORD-MATERIAL-67890", raw);
    }
}
