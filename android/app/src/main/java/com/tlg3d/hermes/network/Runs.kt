package com.tlg3d.hermes.network

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * /v1/runs wire shapes. Verified 2026-10-11 against gateway 0.21.6
 * (RUNS-API.md): live HTTP probes + reads of
 * gateway/platforms/api_server_runs.py. Mirrors the Windows Dtos.Runs
 * shapes; unknown keys are ignored so additive server changes don't break
 * parsing.
 *
 * There is NO list endpoint: GET /v1/runs answers 405. Clients track the run
 * ids they create (or learn from SSE events) and poll per run.
 */
@Serializable
data class RunStatus(
    @SerialName("run_id") val runId: String,
    val status: String,
    @SerialName("created_at") val createdAt: Double? = null,
    @SerialName("updated_at") val updatedAt: Double? = null,
    @SerialName("session_id") val sessionId: String? = null,
    val model: String? = null,
    @SerialName("last_event") val lastEvent: String? = null,
    val output: String? = null,
    val usage: RunUsage? = null,
    val runtime: RunRuntime? = null,
    val completed: Boolean = false,
    val partial: Boolean = false,
    val interrupted: Boolean = false,
    @SerialName("pending_steer") val pendingSteer: String? = null,
) {
    /** Terminal statuses per RUNS-API.md. */
    val isTerminal: Boolean get() = status in TERMINAL_STATUSES

    companion object {
        val TERMINAL_STATUSES = setOf("completed", "failed", "cancelled")
    }
}

@Serializable
data class RunUsage(
    @SerialName("input_tokens") val inputTokens: Long = 0,
    @SerialName("output_tokens") val outputTokens: Long = 0,
    @SerialName("total_tokens") val totalTokens: Long = 0,
    @SerialName("cache_read_tokens") val cacheReadTokens: Long = 0,
    @SerialName("cache_write_tokens") val cacheWriteTokens: Long = 0,
)

@Serializable
data class RunRuntime(
    val provider: String? = null,
    val model: String? = null,
    @SerialName("route_source") val routeSource: String? = null,
)

/** POST /v1/runs/{id}/steer -> 200 {"object":"hermes.run.steer","run_id","accepted"}. */
@Serializable
data class SteerResponse(
    @SerialName("run_id") val runId: String,
    val accepted: Boolean = false,
)
