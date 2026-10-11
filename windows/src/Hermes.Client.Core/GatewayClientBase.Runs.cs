namespace Hermes.Client.Core;

/// <summary>
/// /v1/runs surface on the abstract base: concrete clients implement these
/// with <c>override</c>, following the chat-surface pattern. There is no
/// list endpoint (GET /v1/runs is 405); clients track known run ids.
/// </summary>
public abstract partial class GatewayClientBase
{
    /// <summary>POST /v1/runs (202): start an agent run, return the run id.</summary>
    public abstract Task<RunCreateResponse> CreateRunAsync(
        string input,
        string? sessionId = null,
        string? model = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>GET /v1/runs/{run_id}: pollable run status.</summary>
    public abstract Task<RunStatus> GetRunAsync(
        string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /v1/runs/{run_id}/events (SSE): structured lifecycle events.
    /// </summary>
    public abstract IAsyncEnumerable<RunEvent> StreamRunEventsAsync(
        string runId, long? lastSeq = null, CancellationToken cancellationToken = default);

    /// <summary>POST /v1/runs/{run_id}/steer: inject guidance into a running agent.</summary>
    public abstract Task<SteerResponse> SteerRunAsync(
        string runId, string message, CancellationToken cancellationToken = default);

    /// <summary>POST /v1/runs/{run_id}/stop: interrupt a running agent.</summary>
    public abstract Task<RunStatus> StopRunAsync(
        string runId, CancellationToken cancellationToken = default);

    /// <summary>POST /api/sessions/{id}/fork (201): fork a session.</summary>
    public abstract Task<Session> ForkSessionAsync(
        string sessionId, string? title = null, CancellationToken cancellationToken = default);
}
