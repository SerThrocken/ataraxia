package com.tlg3d.hermes.network

/**
 * Thin-client contract for the Hermes gateway HTTP API (SCOPE.md §2: port 8642).
 *
 * Endpoint paths and response shapes are verified against the live gateway
 * (api-contract.md, 2026-10-11) — no paths are invented here. The concrete
 * implementation is [OkHttpGatewayClient]; it is Android-free (OkHttp +
 * kotlinx.serialization only) so it can be exercised on the JVM.
 */
interface GatewayClient {
    /** Unauthenticated health probe (`GET /health`). */
    suspend fun health(): Boolean

    /**
     * Timed `GET /health` ping. Returns the round-trip latency in
     * milliseconds, or null when the gateway is unreachable / unhealthy.
     * Drives the home-screen widget; the key is never involved (`/health`
     * needs no auth).
     */
    suspend fun healthLatencyMs(): Long?

    /** `GET /api/sessions?limit=…` — newest first. */
    suspend fun listSessions(limit: Int = 50): List<Session>

    /** `POST /api/sessions` — the id lives at `$.session.id`. */
    suspend fun createSession(title: String? = null): Session

    /** `DELETE /api/sessions/{id}`. */
    suspend fun deleteSession(sessionId: String): Boolean

    /** `GET /api/sessions/{id}/messages` — oldest first. */
    suspend fun getMessages(sessionId: String): List<ChatMessage>

    /** `POST /api/sessions/{id}/chat` — one full turn, returns the assistant message. */
    suspend fun sendChat(sessionId: String, message: String): ChatMessage

    /**
     * `POST /api/sessions/{id}/chat/stream` — SSE; emits [StreamEvent]s as they
     * arrive and returns when the server closes the stream (`event: done`).
     * Deltas are append-only per message.
     */
    suspend fun streamChat(sessionId: String, message: String, onEvent: (StreamEvent) -> Unit)

    /**
     * `POST /api/sessions/{id}/chat/stream` with image attachments. The
     * `message` body becomes a parts array (see [buildChatMessageJson]):
     * the gateway's `_normalize_multimodal_content` accepts `image_url`
     * parts with `data:image/…;base64,…` URLs (verified 2026-10-12).
     * Non-streaming `/chat` is text-only on this client.
     */
    suspend fun streamChatImages(
        sessionId: String,
        text: String,
        images: List<ChatImageAttachment>,
        onEvent: (StreamEvent) -> Unit,
    )

    /** `GET /v1/models` — single virtual model `hermes-agent`. */
    suspend fun listModels(): List<ModelInfo>

    /**
     * `GET /v1/skills`. The gateway currently 500s here (verified 2026-10-11);
     * the implementation returns an empty list on 500 and throws otherwise.
     */
    suspend fun listSkills(): List<SkillInfo>

    /**
     * `POST /v1/runs/{run_id}/approval` with `{"choice": …, "request_id"?}`.
     * Resolves an [StreamEvent.Approval]; the paused run then continues.
     */
    suspend fun resolveApproval(runId: String, choice: String, requestId: String? = null)

    /**
     * `GET /v1/runs/{run_id}` — pollable run status. Terminal statuses:
     * `completed` / `failed` / `cancelled` (see [RunStatus.isTerminal]).
     * 404s (`run_not_found`) for unknown or long-forgotten runs.
     */
    suspend fun getRun(runId: String): RunStatus

    /**
     * `POST /v1/runs/{run_id}/steer` with `{"message": …}` — injects guidance
     * into a running agent. Returns the server's `accepted` flag. 409s
     * (`run_not_accepting_steer`) when the run isn't `running`.
     */
    suspend fun steerRun(runId: String, message: String): Boolean

    /**
     * `POST /v1/runs/{run_id}/stop` — interrupts a running agent (in-flight
     * work is discarded). Returns the status object: `{"run_id", "status":
     * "stopping"}` while active, the full status once terminal. 409s
     * (`run_not_active`) when the run isn't live in this gateway process.
     */
    suspend fun stopRun(runId: String): RunStatus
}
