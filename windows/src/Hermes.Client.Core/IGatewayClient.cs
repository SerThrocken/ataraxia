namespace Hermes.Client.Core;

/// <summary>
/// Thin async client over the Hermes CLI gateway (port 8642).
/// Method names mirror the verified endpoint table in api-contract.md.
/// </summary>
public interface IGatewayClient : IAsyncDisposable
{
    // GET /api/sessions (paginated)
    Task<SessionPage> GetSessionsPageAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default);

    // GET/POST /api/sessions
    Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task<Session> CreateSessionAsync(string? title = null, CancellationToken cancellationToken = default);

    // /api/sessions/{id}/messages
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default);

    // POST /api/sessions/{id}/chat  (+ /chat/stream SSE)
    Task<ChatMessage> SendChatAsync(string sessionId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Typed SSE event stream for <c>POST /api/sessions/{id}/chat/stream</c>.
    /// Each yielded event owns its payload; dispose after handling.
    /// </summary>
    IAsyncEnumerable<SseEvent> StreamChatEventsAsync(string sessionId, string message, CancellationToken cancellationToken = default);

    // Legacy raw-delta stream (kept for compatibility).
    IAsyncEnumerable<string> StreamChatAsync(string sessionId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a pending approval: <c>POST /v1/runs/{run_id}/approval</c>
    /// with <c>{"choice": "once"|"session"|"always"|"deny"}</c>. Keyed by run_id.
    /// </summary>
    Task<ApprovalResolution> ResolveApprovalAsync(string runId, string choice, string? requestId = null, CancellationToken cancellationToken = default);

    // GET /v1/artifacts/download/{artifact_id} (one-shot)
    Task<ArtifactDownload> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default);

    // GET /v1/models
    Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);

    // GET /v1/skills
    Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default);

    // GET /health (no auth)
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
