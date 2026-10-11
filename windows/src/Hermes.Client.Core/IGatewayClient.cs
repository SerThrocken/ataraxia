namespace Hermes.Client.Core;

/// <summary>
/// Thin async client over the Hermes CLI gateway (port 8642).
/// Method names mirror the verified endpoint table in api-contract.md.
/// </summary>
public partial interface IGatewayClient : IAsyncDisposable
{
    // GET /api/sessions (paginated)
    Task<SessionPage> GetSessionsPageAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default);

    // GET/POST /api/sessions
    Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task<Session> CreateSessionAsync(string? title = null, CancellationToken cancellationToken = default);

    // GET /api/sessions/{id} — carries input_tokens/output_tokens/estimated_cost_usd
    Task<Session?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    // /api/sessions/{id}/messages
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default);

    // POST /api/sessions/{id}/chat  (+ /chat/stream SSE)
    Task<ChatMessage> SendChatAsync(string sessionId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Typed SSE event stream for <c>POST /api/sessions/{id}/chat/stream</c>.
    /// Each yielded event owns its payload; dispose after handling.
    /// </summary>
    IAsyncEnumerable<SseEvent> StreamChatEventsAsync(string sessionId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="StreamChatEventsAsync"/> but the turn carries a photo:
    /// the request body becomes the OpenAI-style parts array
    /// (<c>text</c> + <c>image_url</c> with a <c>data:image/...;base64,...</c> URL),
    /// which the gateway's <c>_normalize_multimodal_content</c> accepts.
    /// </summary>
    IAsyncEnumerable<SseEvent> StreamChatEventsWithImageAsync(
        string sessionId, string text, ChatImageAttachment image, CancellationToken cancellationToken = default);

    /// <summary>
    /// Non-streaming chat turn with a photo (used by the offline-queue flusher).
    /// </summary>
    Task<ChatMessage> SendChatWithImageAsync(
        string sessionId, string text, ChatImageAttachment image, CancellationToken cancellationToken = default);

    // Legacy raw-delta stream (kept for compatibility).
    IAsyncEnumerable<string> StreamChatAsync(string sessionId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a pending approval: <c>POST /v1/runs/{run_id}/approval</c>
    /// with <c>{"choice": "once"|"session"|"always"|"deny"}</c>. Keyed by run_id.
    /// </summary>
    Task<ApprovalResolution> ResolveApprovalAsync(string runId, string choice, string? requestId = null, CancellationToken cancellationToken = default);

    // GET /v1/artifacts/download/{artifact_id} (one-shot)
    Task<ArtifactDownload> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same download but streams the body and reports byte progress
    /// (<see cref="ArtifactDownloadProgress"/>); <c>TotalBytes</c> is null when
    /// the gateway omits Content-Length, in which case callers stay
    /// indeterminate. Backs the taskbar progress for artifact downloads.
    /// </summary>
    Task<ArtifactDownload> DownloadArtifactWithProgressAsync(
        string artifactId, IProgress<ArtifactDownloadProgress>? progress, CancellationToken cancellationToken = default);

    // GET /v1/models
    Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);

    // GET /v1/skills
    Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default);

    // GET /health (no auth)
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
