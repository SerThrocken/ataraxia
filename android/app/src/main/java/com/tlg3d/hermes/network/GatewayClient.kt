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
}
