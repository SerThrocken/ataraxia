package com.tlg3d.hermes.network

import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.addJsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonArray
import kotlinx.serialization.json.putJsonObject

/**
 * Serializes the chat-stream request body. Text-only turns keep the exact
 * legacy wire shape (`{"message":"…"}`); with images the body switches to
 * the OpenAI-style parts array the gateway's `_normalize_multimodal_content`
 * accepts (verified against gateway source, 2026-10-12): text + `image_url`
 * parts carrying `data:image/…;base64,…` URLs.
 */
fun buildChatMessageJson(text: String, images: List<ChatImageAttachment>): String {
    if (images.isEmpty()) {
        return HermesJson.encodeToString(buildJsonObject { put("message", text) })
    }
    return HermesJson.encodeToString(buildJsonObject {
        putJsonArray("message") {
            addJsonObject {
                put("type", "text")
                put("text", text)
            }
            images.forEach { image ->
                addJsonObject {
                    put("type", "image_url")
                    putJsonObject("image_url") { put("url", image.toDataUrl()) }
                }
            }
        }
    })
}
