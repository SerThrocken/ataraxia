namespace Hermes.Client.Core;

/// <summary>
/// The two members a gateway client exposes for pointing it at a
/// connection. <see cref="GatewayClientBase"/> implements this; the
/// <see cref="GatewaySwitcher"/> programs against the interface so the
/// switching logic stays unit-testable without HTTP.
/// </summary>
public interface IGatewayEndpoint
{
    void SetBaseUrl(Uri baseUrl);
    void SetApiKey(string? apiKey);
}
