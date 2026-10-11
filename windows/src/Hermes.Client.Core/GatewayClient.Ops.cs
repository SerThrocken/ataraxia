namespace Hermes.Client.Core;

/// <summary>
/// Ops surface of the gateway client: health detail and full session
/// iteration. Implemented on the concrete class so every consumer shares
/// the plumbing in <see cref="GatewayClientBase"/>.
/// </summary>
public sealed partial class GatewayClient
{
    /// <inheritdoc cref="GatewayClientBase.GetDetailedHealthAsync"/>
    public override async Task<DetailedHealth> GetDetailedHealthAsync(CancellationToken cancellationToken = default)
    {
        var health = await GetJsonAsync<DetailedHealth>("health/detailed", cancellationToken)
            .ConfigureAwait(false);
        return health ?? throw new GatewayApiException(200, "Empty detailed-health response.");
    }

    /// <inheritdoc cref="GatewayClientBase.GetAllSessionsAsync"/>
    public override async Task<IReadOnlyList<Session>> GetAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        const int limit = 100;
        var all = new List<Session>();
        var offset = 0;
        while (true)
        {
            var page = await GetSessionsPageAsync(limit, offset, cancellationToken).ConfigureAwait(false);
            all.AddRange(page.Sessions);
            if (!page.HasMore)
            {
                break;
            }

            offset += limit;
        }

        return all;
    }
}
