package com.tlg3d.hermes.network

import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

/**
 * Wire-shape tests for [buildChatMessageJson]. The gateway's
 * `_normalize_multimodal_content` (api_server.py) accepts a plain string for
 * text-only turns and an OpenAI-style parts array otherwise; the image part
 * must be `{"type": "image_url", "image_url": {"url": "data:image/…"}}`.
 */
class ChatMessagePayloadTest {

    private fun bodyOf(json: String) =
        HermesJson.parseToJsonElement(json).jsonObject

    @Test
    fun `text-only payload keeps the legacy wire shape`() {
        val body = bodyOf(buildChatMessageJson("hello", emptyList()))
        assertEquals("hello", body["message"]!!.jsonPrimitive.content)
        assertEquals(1, body.size)
    }

    @Test
    fun `image payload uses a parts array with text first`() {
        val image = ChatImageAttachment(byteArrayOf(1, 2, 3), "image/jpeg")
        val parts = bodyOf(buildChatMessageJson("look", listOf(image)))["message"]!!.jsonArray

        assertEquals(2, parts.size)
        assertEquals("text", parts[0].jsonObject["type"]!!.jsonPrimitive.content)
        assertEquals("look", parts[0].jsonObject["text"]!!.jsonPrimitive.content)

        val imagePart = parts[1].jsonObject
        assertEquals("image_url", imagePart["type"]!!.jsonPrimitive.content)
        assertEquals(
            "data:image/jpeg;base64,AQID",
            imagePart["image_url"]!!.jsonObject["url"]!!.jsonPrimitive.content,
        )
    }

    @Test
    fun `each image becomes its own image_url part in order`() {
        val images = listOf(
            ChatImageAttachment(byteArrayOf(1), "image/jpeg"),
            ChatImageAttachment(byteArrayOf(2), "image/png"),
        )
        val parts = bodyOf(buildChatMessageJson("", images))["message"]!!.jsonArray

        assertEquals(3, parts.size)
        assertEquals(
            "data:image/jpeg;base64,AQ==",
            parts[1].jsonObject["image_url"]!!.jsonObject["url"]!!.jsonPrimitive.content,
        )
        assertEquals(
            "data:image/png;base64,Ag==",
            parts[2].jsonObject["image_url"]!!.jsonObject["url"]!!.jsonPrimitive.content,
        )
    }

    @Test
    fun `empty text with an image still carries a text part`() {
        val image = ChatImageAttachment(byteArrayOf(9), "image/jpeg")
        val parts = bodyOf(buildChatMessageJson("", listOf(image)))["message"]!!.jsonArray

        assertEquals(2, parts.size)
        assertEquals("text", parts[0].jsonObject["type"]!!.jsonPrimitive.content)
        assertEquals("", parts[0].jsonObject["text"]!!.jsonPrimitive.content)
    }

    @Test
    fun `attachment rejects empty data`() {
        assertThrows(IllegalArgumentException::class.java) {
            ChatImageAttachment(byteArrayOf(), "image/jpeg")
        }
    }

    @Test
    fun `attachment rejects non-image mime types`() {
        assertThrows(IllegalArgumentException::class.java) {
            ChatImageAttachment(byteArrayOf(1), "text/plain")
        }
    }

    @Test
    fun `toDataUrl builds a gateway-accepted data URL`() {
        assertEquals(
            "data:image/png;base64,AQID",
            ChatImageAttachment(byteArrayOf(1, 2, 3), "image/png").toDataUrl(),
        )
    }
}
