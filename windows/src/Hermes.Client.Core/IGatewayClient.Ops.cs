namespace Hermes.Client.Core;

public partial interface IGatewayClient
{
    // GET /health/detailed (Bearer): readiness checks, disk usage,
    // background queues, and the per-platform connection table.
    Task<DetailedHealth> GetDetailedHealthAsync(CancellationToken cancellationToken = default);

    // Full session iteration (paged until has_more is false); backs the
    // cost-analytics rollup — the gateway has no usage endpoint on 8642.
    Task<IReadOnlyList<Session>> GetAllSessionsAsync(CancellationToken cancellationToken = default);
}
