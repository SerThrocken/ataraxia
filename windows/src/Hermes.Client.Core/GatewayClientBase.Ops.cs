namespace Hermes.Client.Core;

/// <summary>
/// Ops surface on the abstract base: concrete clients implement these
/// with <c>override</c>, following the chat-surface pattern.
/// </summary>
public abstract partial class GatewayClientBase
{
    /// <summary>
    /// GET /health/detailed (Bearer). Verified shape 2026-10-11 against
    /// gateway 0.21.6: readiness checks, disk usage, background queues, and
    /// the 13-entry platform table. No CPU/memory/uptime on this endpoint.
    /// </summary>
    public abstract Task<DetailedHealth> GetDetailedHealthAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Iterates every session via GET /api/sessions?limit/offset until
    /// <c>has_more</c> is false. Used by the analytics page; the gateway
    /// exposes no server-side usage aggregation endpoint.
    /// </summary>
    public abstract Task<IReadOnlyList<Session>> GetAllSessionsAsync(
        CancellationToken cancellationToken = default);
}
