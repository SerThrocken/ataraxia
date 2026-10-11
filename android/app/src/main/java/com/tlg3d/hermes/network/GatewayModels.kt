package com.tlg3d.hermes.network

import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.descriptors.PrimitiveKind
import kotlinx.serialization.descriptors.PrimitiveSerialDescriptor
import kotlinx.serialization.descriptors.SerialDescriptor
import kotlinx.serialization.encoding.Decoder
import kotlinx.serialization.encoding.Encoder
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/**
 * Wire models for the Hermes gateway API (CLI gateway, port 8642).
 *
 * Shapes verified live against the gateway on 2026-10-11 (probe + api-contract.md):
 * sessions/messages/models/health. Unknown keys are ignored so additive
 * server changes don't break parsing.
 */
val HermesJson = Json {
    ignoreUnknownKeys = true
    isLenient = true
}

@Serializable
data class Session(
    val id: String,
    val title: String? = null,
    val preview: String? = null,
    @SerialName("message_count") val messageCount: Int = 0,
    val model: String? = null,
    @SerialName("started_at") val startedAt: Double? = null,
    @SerialName("last_active") val lastActive: Double? = null,
    val pinned: Boolean = false,
    val archived: Boolean = false,
    @SerialName("input_tokens") val inputTokens: Long = 0,
    @SerialName("output_tokens") val outputTokens: Long = 0,
    @SerialName("estimated_cost_usd") val estimatedCostUsd: Double? = null,
)

/**
 * Message content arrives as a plain string in the common case, but the
 * gateway may send a parts array. This serializer accepts both, extracting
 * text from parts objects.
 */
object FlexibleContentSerializer : KSerializer<String> {
    override val descriptor: SerialDescriptor =
        PrimitiveSerialDescriptor("FlexibleContent", PrimitiveKind.STRING)

    override fun serialize(encoder: Encoder, value: String) {
        encoder.encodeString(value)
    }

    override fun deserialize(decoder: Decoder): String {
        val element = (decoder as? kotlinx.serialization.json.JsonDecoder)
            ?.decodeJsonElement()
            ?: return decoder.decodeString()
        return when (element) {
            is JsonPrimitive -> element.content
            is JsonArray -> element.joinToString("") { part ->
                val obj = part as? JsonObject
                obj?.get("text")?.jsonPrimitive?.content
                    ?: obj?.get("content")?.jsonPrimitive?.content
                    ?: ""
            }
            else -> ""
        }
    }
}

@Serializable
data class ChatMessage(
    val role: String,
    @Serializable(with = FlexibleContentSerializer::class)
    val content: String = "",
    val id: String? = null,
    @SerialName("session_id") val sessionId: String? = null,
    val timestamp: Double? = null,
    @SerialName("finish_reason") val finishReason: String? = null,
    val reasoning: String? = null,
    @SerialName("tool_name") val toolName: String? = null,
)

@Serializable
data class ModelInfo(
    val id: String,
    val name: String? = null,
    val provider: String? = null,
)

@Serializable
data class SkillInfo(
    val name: String,
    val description: String? = null,
    val category: String? = null,
    val id: String? = null,
)

@Serializable
data class TokenUsage(
    @SerialName("input_tokens") val inputTokens: Long = 0,
    @SerialName("output_tokens") val outputTokens: Long = 0,
    @SerialName("total_tokens") val totalTokens: Long = 0,
)

@Serializable
data class ListEnvelope<T>(
    val data: List<T> = emptyList(),
)

@Serializable
data class SessionCreateResponse(
    val session: Session,
)

@Serializable
data class MessagesEnvelope(
    @SerialName("session_id") val sessionId: String? = null,
    val data: List<ChatMessage> = emptyList(),
)

@Serializable
data class ChatResponse(
    val message: ChatMessage? = null,
    val usage: TokenUsage? = null,
)

/** A pending dangerous-command approval from an `approval.request` SSE event. */
data class ApprovalRequest(
    val runId: String,
    val sessionId: String? = null,
    val messageId: String? = null,
    val command: String? = null,
    val toolName: String? = null,
    val description: String? = null,
    val choices: List<String> = emptyList(),
    val requestId: String? = null,
)

/** Stream events surfaced by [GatewayClient.streamChat]. */
sealed interface StreamEvent {
    /** Incremental assistant text. */
    data class Delta(val text: String) : StreamEvent

    /** Incremental reasoning (`tool.progress` with `tool_name: "_thinking"`). */
    data class Reasoning(val text: String) : StreamEvent

    /** Final assistant text for the turn. */
    data class Completed(val content: String, val usage: TokenUsage? = null) : StreamEvent

    /** The run needs a dangerous-command approval; the stream pauses until resolved. */
    data class Approval(val request: ApprovalRequest) : StreamEvent

    /** The turn failed server-side. */
    data class Failed(val message: String) : StreamEvent
}

/** HTTP/API failure talking to the gateway. Never carries the API key. */
class GatewayException(message: String, val statusCode: Int? = null) : Exception(message)
