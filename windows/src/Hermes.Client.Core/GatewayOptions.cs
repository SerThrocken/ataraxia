namespace Hermes.Client.Core;

/// <summary>
/// Connection options for the Hermes gateway. The API key is a bearer token
/// (≥16 chars, no loopback exemption on 8642) and must be loaded from the
/// secret store, never from plaintext config.
/// </summary>
public sealed record GatewayOptions
{
    public required Uri BaseUrl { get; init; } = new Uri("http://localhost:8642");

    public string? ApiKey { get; init; }
}
