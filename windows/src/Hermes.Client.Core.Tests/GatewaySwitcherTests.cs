using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Switching gateways must tear down the old connection's live resources
/// (SSE streams, run subscriptions) BEFORE the client is re-pointed — no
/// session leakage, no cross-talk between gateways.
/// </summary>
public sealed class GatewaySwitcherTests
{
    private readonly FakeSettingsService _settings = new();
    private readonly FakeSecretStore _secrets = new();
    private GatewayConnectionRegistry Registry => new(_settings, _secrets);

    private async Task<(GatewayConnectionRegistry registry, string acerId, string cloudId)>
        TwoGatewaysAsync()
    {
        var registry = Registry;
        var acerId = await registry.AddAsync(
            new GatewayConnection { Name = "Acer", Host = "100.101.250.118" });
        await registry.SetApiKeyAsync(acerId, "acer-key-1234567890");
        var cloudId = await registry.AddAsync(
            new GatewayConnection { Name = "Cloud", Host = "gateway.example.com", UseTls = true });
        await registry.SetApiKeyAsync(cloudId, "cloud-key-1234567890");
        return (registry, acerId, cloudId);
    }

    [Fact]
    public async Task SwitchToAsync_TearsDownOldResources_BeforeApplyingNew()
    {
        var (registry, acerId, cloudId) = await TwoGatewaysAsync();
        var switcher = new GatewaySwitcher(registry);
        var endpoint = new FakeGatewayEndpoint();
        await switcher.SwitchToAsync(acerId, endpoint, liveResources: null);

        var order = new List<string>();
        var resources = new FakeLiveResources(() => order.Add("disposed"));

        // Wrap the endpoint to record when the new connection is applied.
        var recording = new RecordingEndpoint(endpoint, () => order.Add("applied"));

        var outcome = await switcher.SwitchToAsync(cloudId, recording, resources);

        Assert.Equal(SwitchOutcome.Switched, outcome);
        Assert.True(resources.Disposed);
        Assert.Equal(new[] { "disposed", "applied" }, order);
        Assert.Equal("https://gateway.example.com:8642/", endpoint.BaseUrls[^1].ToString());
        Assert.Equal("cloud-key-1234567890", endpoint.ApiKeys[^1]);
        Assert.Equal(cloudId, (await registry.GetActiveAsync())!.Id);
        Assert.Equal(cloudId, switcher.Active!.Id);
    }

    [Fact]
    public async Task SwitchToAsync_SameId_IsNoOp()
    {
        var (registry, acerId, _) = await TwoGatewaysAsync();
        var switcher = new GatewaySwitcher(registry);
        var endpoint = new FakeGatewayEndpoint();
        await switcher.SwitchToAsync(acerId, endpoint, liveResources: null);

        var resources = new FakeLiveResources(() => { });
        var outcome = await switcher.SwitchToAsync(acerId, endpoint, resources);

        Assert.Equal(SwitchOutcome.AlreadyActive, outcome);
        Assert.False(resources.Disposed);
        Assert.Single(endpoint.BaseUrls); // only the first switch applied
    }

    [Fact]
    public async Task SwitchToAsync_UnknownId_Throws()
    {
        var (registry, _, _) = await TwoGatewaysAsync();
        var switcher = new GatewaySwitcher(registry);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            switcher.SwitchToAsync("nope", new FakeGatewayEndpoint(), liveResources: null));
    }

    [Fact]
    public async Task SwitchToAsync_RaisesSwitched_WithNewConnection()
    {
        var (registry, _, cloudId) = await TwoGatewaysAsync();
        var switcher = new GatewaySwitcher(registry);
        GatewayConnection? raised = null;
        switcher.Switched += (_, c) => raised = c;

        await switcher.SwitchToAsync(cloudId, new FakeGatewayEndpoint(), liveResources: null);

        Assert.NotNull(raised);
        Assert.Equal(cloudId, raised.Id);
    }

    [Fact]
    public async Task SwitchToAsync_NullResources_StillSwitches()
    {
        var (registry, _, cloudId) = await TwoGatewaysAsync();
        var switcher = new GatewaySwitcher(registry);
        var endpoint = new FakeGatewayEndpoint();

        var outcome = await switcher.SwitchToAsync(cloudId, endpoint, liveResources: null);

        Assert.Equal(SwitchOutcome.Switched, outcome);
        Assert.Equal("https://gateway.example.com:8642/", endpoint.BaseUrls[^1].ToString());
    }

    [Fact]
    public async Task InitializeAsync_MigratesLegacy_AndAppliesActive()
    {
        await _settings.SaveAsync("connection",
            new LegacyConnectionSettings { BaseUrl = "http://100.101.250.118:8642" });
        await _secrets.SetAsync("gateway-api-key", "legacy-key-1234567890");
        var switcher = new GatewaySwitcher(Registry);
        var endpoint = new FakeGatewayEndpoint();

        await switcher.InitializeAsync(endpoint, "connection", "gateway-api-key");

        Assert.NotNull(switcher.Active);
        Assert.Equal("100.101.250.118", switcher.Active.Host);
        Assert.Equal("http://100.101.250.118:8642/", endpoint.BaseUrls[^1].ToString());
        Assert.Equal("legacy-key-1234567890", endpoint.ApiKeys[^1]);
    }

    [Fact]
    public async Task InitializeAsync_LeavesEndpointAlone_WhenNothingSaved()
    {
        var switcher = new GatewaySwitcher(Registry);
        var endpoint = new FakeGatewayEndpoint();

        await switcher.InitializeAsync(endpoint, "connection", "gateway-api-key");

        Assert.Null(switcher.Active);
        Assert.Empty(endpoint.BaseUrls);
    }

    /// <summary>Wraps an endpoint to observe exactly when the new connection is applied.</summary>
    private sealed class RecordingEndpoint(FakeGatewayEndpoint inner, Action onApply) : IGatewayEndpoint
    {
        public void SetBaseUrl(Uri baseUrl)
        {
            onApply();
            inner.SetBaseUrl(baseUrl);
        }

        public void SetApiKey(string? apiKey) => inner.SetApiKey(apiKey);
    }
}
