using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// ElectronRegistryReader: pure parsing of the Electron desktop app's
/// on-disk state. Fixtures mirror the real shapes observed on the Acer
/// 2026-10-11 (connections.json v2, connection.json, secure-token-storage
/// flags, native-theme.json, window-state.json, gateway .env).
/// </summary>
public sealed class ElectronImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "electron-import-tests-" + Guid.NewGuid().ToString("N"));

    public ElectronImportTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_dir, name), content);

    [Fact]
    public void ReadRegistry_ParsesV2Registry()
    {
        Write("connections.json", """
            {
              "version": 2,
              "primary": "local",
              "launchMode": "primary",
              "lastUsed": "local",
              "connections": [
                { "id": "local", "kind": "local", "label": "This device" },
                { "id": "tail", "kind": "remote", "label": "Acer over Tailscale", "url": "http://100.101.250.118:8642" }
              ]
            }
            """);

        var registry = ElectronRegistryReader.ReadRegistry(_dir);

        Assert.Equal(2, registry.Version);
        Assert.Equal("local", registry.PrimaryId);
        Assert.Equal("local", registry.LastUsedId);
        Assert.Equal(2, registry.Connections.Count);
        Assert.Equal(new ElectronConnection("local", "local", "This device", null), registry.Connections[0]);
        Assert.Equal(
            new ElectronConnection("tail", "remote", "Acer over Tailscale", "http://100.101.250.118:8642"),
            registry.Connections[1]);
    }

    [Fact]
    public void ReadRegistry_MissingFile_YieldsEmptyRegistry()
    {
        var registry = ElectronRegistryReader.ReadRegistry(_dir);

        Assert.Empty(registry.Connections);
        Assert.Null(registry.PrimaryId);
        Assert.Equal(0, registry.Version);
    }

    [Fact]
    public void ReadRegistry_MalformedJson_YieldsEmptyRegistry()
    {
        Write("connections.json", "{ this is not json");

        var registry = ElectronRegistryReader.ReadRegistry(_dir);

        Assert.Empty(registry.Connections);
    }

    [Fact]
    public void ReadRegistry_SkipsNonObjectEntries()
    {
        Write("connections.json", """{ "version": 2, "connections": [42, { "id": "ok", "kind": "local", "label": "L" }] }""");

        var registry = ElectronRegistryReader.ReadRegistry(_dir);

        Assert.Single(registry.Connections);
        Assert.Equal("ok", registry.Connections[0].Id);
    }

    [Fact]
    public void ReadLegacyRemoteConnection_ParsesRemoteProfile()
    {
        Write("connection.json", """{ "mode": "local", "remote": { "url": "http://example:8642", "authMode": "token" }, "profiles": {} }""");

        var connection = ElectronRegistryReader.ReadLegacyRemoteConnection(_dir);

        Assert.NotNull(connection);
        Assert.Equal("remote", connection.Kind);
        Assert.Equal("http://example:8642", connection.Url);
    }

    [Fact]
    public void ReadLegacyRemoteConnection_EmptyUrl_ReturnsNull()
    {
        Write("connection.json", """{ "mode": "local", "remote": { "url": "", "authMode": "token" } }""");

        Assert.Null(ElectronRegistryReader.ReadLegacyRemoteConnection(_dir));
    }

    [Fact]
    public void ReadLegacyRemoteConnection_MissingFile_ReturnsNull()
    {
        Assert.Null(ElectronRegistryReader.ReadLegacyRemoteConnection(_dir));
    }

    [Fact]
    public void ReadTokenStorage_ReturnsFlatMap()
    {
        // Real file holds only flags ("on"/"migrated"); tokens would ride
        // alongside them as extra keys. Values are never asserted on content
        // beyond round-tripping — nothing here may leak into logs.
        Write("secure-token-storage.json", """{ "on": "false", "migrated": "true", "gateway-token": "secret-value-xyz" }""");

        var tokens = ElectronRegistryReader.ReadTokenStorage(_dir);

        Assert.Equal(3, tokens.Count);
        Assert.Equal("secret-value-xyz", tokens["gateway-token"]);
    }

    [Fact]
    public void ReadTokenStorage_MissingFile_ReturnsEmptyMap()
    {
        Assert.Empty(ElectronRegistryReader.ReadTokenStorage(_dir));
    }

    [Fact]
    public void ReadGatewayApiKey_ParsesEnvFile()
    {
        Write(".env", "SOME_OTHER=1\nAPI_SERVER_KEY=the-key-value-here\nANOTHER=2\n");

        Assert.Equal("the-key-value-here", ElectronRegistryReader.ReadGatewayApiKey(_dir));
    }

    [Fact]
    public void ReadGatewayApiKey_MissingKey_ReturnsNull()
    {
        Write(".env", "SOME_OTHER=1\n");

        Assert.Null(ElectronRegistryReader.ReadGatewayApiKey(_dir));
    }

    [Fact]
    public void ReadGatewayApiKey_MissingFile_ReturnsNull()
    {
        Assert.Null(ElectronRegistryReader.ReadGatewayApiKey(_dir));
    }

    [Fact]
    public void ReadDesktopSettings_ParsesThemeAndWindowBounds()
    {
        Write("native-theme.json", """{ "themeSource": "dark" }""");
        Write("window-state.json", """{ "x": 0, "y": 0, "width": 854, "height": 920, "isMaximized": false }""");

        var settings = ElectronRegistryReader.ReadDesktopSettings(_dir);

        Assert.Equal("dark", settings.Theme);
        Assert.Equal(854, settings.WindowWidth);
        Assert.Equal(920, settings.WindowHeight);
        Assert.False(settings.WindowMaximized);
    }

    [Fact]
    public void ReadDesktopSettings_MissingFiles_YieldsNulls()
    {
        var settings = ElectronRegistryReader.ReadDesktopSettings(_dir);

        Assert.Null(settings.Theme);
        Assert.Null(settings.WindowWidth);
        Assert.Null(settings.WindowHeight);
        Assert.Null(settings.WindowMaximized);
    }
}
