namespace Hermes.Client.Core;

public partial interface IGatewayClient
{
    /// <summary>
    /// Starts an agent run: <c>POST /v1/runs</c> (202) with
    /// <c>{"input", "session_id"?, "model"?}</c> and an optional
    /// <c>Idempotency-Key</c> header. Returns the run id immediately;
    /// the run executes asynchronously.
    /// </summary>
    Task<RunCreateResponse> CreateRunAsync(
        string input,
        string? sessionId = null,
        string? model = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pollable run status: <c>GET /v1/runs/{run_id}</c>.
    /// Terminal when <see cref="RunStatus.IsTerminal"/>.
    /// </summary>
    Task<RunStatus> GetRunAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Structured lifecycle events: <c>GET /v1/runs/{run_id}/events</c> (SSE).
    /// Pass the last seen seq to resume after a disconnect
    /// (<c>?last_seq=N</c>). Each event is a retained record; the raw SSE
    /// frames are disposed by the client.
    /// </summary>
    IAsyncEnumerable<RunEvent> StreamRunEventsAsync(
        string runId, long? lastSeq = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects guidance into a running agent:
    /// <c>POST /v1/runs/{run_id}/steer</c> with <c>{"message": …}</c>.
    /// 409 (<c>run_not_accepting_steer</c>) when the run is not running.
    /// </summary>
    Task<SteerResponse> SteerRunAsync(
        string runId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupts a running agent: <c>POST /v1/runs/{run_id}/stop</c>.
    /// Answers <c>{"run_id","status":"stopping"}</c> while active, or the
    /// full terminal <see cref="RunStatus"/> when already finished.
    /// </summary>
    Task<RunStatus> StopRunAsync(string runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forks a session: <c>POST /api/sessions/{id}/fork</c> (201).
    /// The returned session carries <c>ParentSessionId</c> for fork-tree
    /// rendering.
    /// </summary>
    Task<Session> ForkSessionAsync(
        string sessionId, string? title = null, CancellationToken cancellationToken = default);
}
