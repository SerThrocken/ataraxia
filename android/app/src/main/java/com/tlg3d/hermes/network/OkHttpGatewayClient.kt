package com.tlg3d.hermes.network

import java.util.concurrent.TimeUnit
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody

private val JSON_MEDIA_TYPE = "application/json; charset=utf-8".toMediaType()

/** One parsed SSE frame. Pure data — see [SseParser]. */
internal data class SseFrame(val event: String, val data: String)

/**
 * Incremental SSE frame parser (the gateway wire format is
 * `event: <name>\n` + `data: <json>\n\n`, with `: keepalive` comments).
 * Pure logic, no I/O — safe to exercise on the JVM.
 */
internal class SseParser {
    private var event: String? = null
    private val data = StringBuilder()

    /** Feeds one line (without the line terminator); returns a frame on blank lines. */
    fun feedLine(line: String): SseFrame? {
        if (line.isEmpty()) {
            val frame = if (event != null || data.isNotEmpty()) {
                SseFrame(event ?: "message", data.toString())
            } else {
                null
            }
            event = null
            data.clear()
            return frame
        }
        if (line.startsWith(":")) return null // keepalive comment
        val idx = line.indexOf(':')
        if (idx < 0) return null
        val field = line.substring(0, idx)
        var value = line.substring(idx + 1)
        if (value.startsWith(" ")) value = value.substring(1)
        when (field) {
            "event" -> event = value
            "data" -> {
                if (data.isNotEmpty()) data.append('\n')
                data.append(value)
            }
        }
        return null
    }
}

/**
 * [GatewayClient] over OkHttp. Android-free: only OkHttp and
 * kotlinx.serialization, so the same code runs on the JVM for verification.
 *
 * The API key travels only in the `Authorization` header; it is never logged
 * or included in exception messages.
 */
class OkHttpGatewayClient(
    val config: GatewayConfig,
    private val http: OkHttpClient = defaultHttpClient(),
) : GatewayClient {

    private val base: String = config.baseUrl.trimEnd('/')

    override suspend fun health(): Boolean {
        val request = Request.Builder().url("$base/health").get().build()
        http.newCall(request).execute().use { return it.isSuccessful }
    }

    override suspend fun listSessions(limit: Int): List<Session> {
        val body = get("/api/sessions?limit=$limit")
        return HermesJson.decodeFromString<ListEnvelope<Session>>(body).data
    }

    override suspend fun createSession(title: String?): Session {
        val payload = buildJsonObject { if (title != null) put("title", title) }
        val body = post("/api/sessions", HermesJson.encodeToString(payload))
        return HermesJson.decodeFromString<SessionCreateResponse>(body).session
    }

    override suspend fun deleteSession(sessionId: String): Boolean {
        val body = delete("/api/sessions/$sessionId")
        return runCatching {
            HermesJson.parseToJsonElement(body).jsonObject["deleted"]?.jsonPrimitive?.content == "true"
        }.getOrDefault(false)
    }

    override suspend fun getMessages(sessionId: String): List<ChatMessage> {
        val body = get("/api/sessions/$sessionId/messages")
        return HermesJson.decodeFromString<MessagesEnvelope>(body).data
    }

    override suspend fun sendChat(sessionId: String, message: String): ChatMessage {
        val payload = buildJsonObject { put("message", message) }
        val body = post("/api/sessions/$sessionId/chat", HermesJson.encodeToString(payload))
        return HermesJson.decodeFromString<ChatResponse>(body).message
            ?: throw GatewayException("Chat response carried no message.")
    }

    override suspend fun streamChat(
        sessionId: String,
        message: String,
        onEvent: (StreamEvent) -> Unit,
    ) {
        val payload = buildJsonObject { put("message", message) }
        val request = Request.Builder()
            .url("$base/api/sessions/$sessionId/chat/stream")
            .post(HermesJson.encodeToString(payload).toRequestBody(JSON_MEDIA_TYPE))
            .header("Accept", "text/event-stream")
            .apply { authHeader()?.let { header("Authorization", it) } }
            .build()
        // Long turns stream for minutes; the default read timeout would kill them.
        val streamHttp = http.newBuilder().readTimeout(10, TimeUnit.MINUTES).build()
        streamHttp.newCall(request).execute().use { response ->
            checkOk(response.code, response.message, "POST /api/sessions/{id}/chat/stream")
            val parser = SseParser()
            val text = StringBuilder()
            var completedEmitted = false
            val source = response.body!!.source()
            while (true) {
                val line = source.readUtf8Line() ?: break
                val frame = parser.feedLine(line) ?: continue
                if (dispatch(frame, text, onEvent)) completedEmitted = true
            }
            if (!completedEmitted && text.isNotEmpty()) {
                onEvent(StreamEvent.Completed(text.toString()))
            }
        }
    }

    /**
     * Dispatches one SSE frame. Returns true when the terminal `done` frame
     * arrived so the caller can stop synthesizing a fallback completion.
     */
    private fun dispatch(
        frame: SseFrame,
        text: StringBuilder,
        onEvent: (StreamEvent) -> Unit,
    ): Boolean {
        val data = runCatching { HermesJson.parseToJsonElement(frame.data).jsonObject }
            .getOrNull() ?: return false
        fun str(key: String): String? = (data[key] as? JsonPrimitive)?.content

        when (frame.event) {
            "assistant.delta" -> {
                val delta = str("delta") ?: return false
                text.append(delta)
                onEvent(StreamEvent.Delta(delta))
            }
            "tool.progress" -> {
                if (str("tool_name") == "_thinking") {
                    val delta = str("delta") ?: return false
                    onEvent(StreamEvent.Reasoning(delta))
                }
            }
            "assistant.commentary" -> {
                val already = data["already_streamed"]?.jsonPrimitive?.content == "true"
                val comment = str("text")
                if (!already && comment != null) {
                    text.append(comment)
                    onEvent(StreamEvent.Delta(comment))
                }
            }
            "assistant.completed" -> {
                val content = str("content") ?: text.toString()
                onEvent(StreamEvent.Completed(content))
            }
            "run.completed" -> {
                val messages = data["messages"]?.jsonArray.orEmpty()
                val content = messages.firstOrNull()?.jsonObject
                    ?.get("content")?.jsonPrimitive?.content ?: text.toString()
                val usage = runCatching {
                    HermesJson.decodeFromJsonElement(TokenUsage.serializer(), data["usage"]!!)
                }.getOrNull()
                onEvent(StreamEvent.Completed(content, usage))
            }
            "run.cancelled", "run.failed" -> {
                onEvent(StreamEvent.Failed(str("message") ?: "The run ${frame.event.removePrefix("run.")}."))
            }
            "approval.request" -> {
                val choices = data["choices"]?.jsonArray.orEmpty()
                    .mapNotNull { it.jsonPrimitive.content }
                onEvent(
                    StreamEvent.Approval(
                        ApprovalRequest(
                            runId = str("run_id") ?: "",
                            sessionId = str("session_id"),
                            messageId = str("message_id"),
                            command = str("command"),
                            toolName = str("tool_name"),
                            description = str("description") ?: str("reason"),
                            choices = choices,
                            requestId = str("request_id"),
                        ),
                    ),
                )
            }
            "error" -> onEvent(StreamEvent.Failed(str("message") ?: "Stream error."))
            "done" -> return true
        }
        return false
    }

    override suspend fun listModels(): List<ModelInfo> {
        val body = get("/v1/models")
        return HermesJson.decodeFromString<ListEnvelope<ModelInfo>>(body).data
    }

    override suspend fun listSkills(): List<SkillInfo> {
        return try {
            val body = get("/v1/skills")
            HermesJson.decodeFromString<ListEnvelope<SkillInfo>>(body).data
        } catch (e: GatewayException) {
            // Verified 2026-10-11: the gateway 500s here (include_editorial bug).
            if (e.statusCode == 500) emptyList() else throw e
        }
    }

    override suspend fun resolveApproval(runId: String, choice: String, requestId: String?) {
        val payload = buildJsonObject {
            put("choice", choice)
            if (requestId != null) put("request_id", requestId)
        }
        post("/v1/runs/$runId/approval", HermesJson.encodeToString(payload))
    }

    // -- HTTP plumbing -------------------------------------------------------

    private fun authHeader(): String? =
        config.apiKey.takeIf { it.isNotBlank() }?.let { "Bearer $it" }

    private fun authorized(builder: Request.Builder): Request.Builder {
        authHeader()?.let { builder.header("Authorization", it) }
        return builder
    }

    private fun get(path: String): String {
        val request = authorized(Request.Builder().url("$base$path")).get().build()
        http.newCall(request).execute().use { response ->
            checkOk(response.code, response.message, "GET $path")
            return response.body!!.string()
        }
    }

    private fun post(path: String, json: String): String {
        val request = authorized(Request.Builder().url("$base$path"))
            .post(json.toRequestBody(JSON_MEDIA_TYPE))
            .build()
        http.newCall(request).execute().use { response ->
            checkOk(response.code, response.message, "POST $path")
            return response.body!!.string()
        }
    }

    private fun delete(path: String): String {
        val request = authorized(Request.Builder().url("$base$path")).delete().build()
        http.newCall(request).execute().use { response ->
            checkOk(response.code, response.message, "DELETE $path")
            return response.body!!.string()
        }
    }

    private fun checkOk(code: Int, message: String, what: String) {
        if (code in 200..299) return
        if (code == 401) {
            throw GatewayException(
                "Gateway rejected the API key (401) for $what. Check the key in Settings.",
                401,
            )
        }
        throw GatewayException("$what failed: HTTP $code $message", code)
    }

    companion object {
        fun defaultHttpClient(): OkHttpClient = OkHttpClient.Builder()
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(60, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .build()
    }
}
