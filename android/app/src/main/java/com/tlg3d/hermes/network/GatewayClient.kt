package com.tlg3d.hermes.network

/**
 * Thin-client contract for the Hermes gateway HTTP API (SCOPE.md §2: port 8642).
 *
 * Interface only. The OkHttp-backed implementation (including SSE streaming
 * for `/api/sessions/{id}/chat/stream` and the `/api/ws` JSON-RPC channel)
 * gets dropped in once api-contract.md pins the response shapes — no endpoint
 * paths are invented here.
 */
interface GatewayClient {
    /** Unauthenticated health probe (`/health`, `/v1/health`). */
    suspend fun health(): Boolean

    suspend fun listSessions(): List<Session>
    suspend fun createSession(title: String? = null): Session
    suspend fun getMessages(sessionId: String): List<ChatMessage>
    suspend fun sendChat(sessionId: String, message: ChatMessage): ChatMessage

    suspend fun listModels(): List<ModelInfo>
    suspend fun listSkills(): List<SkillInfo>
}
