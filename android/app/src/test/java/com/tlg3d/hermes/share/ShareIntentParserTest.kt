package com.tlg3d.hermes.share

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ShareIntentParserTest {

    @Test
    fun `plain text share yields a text payload`() {
        val payload = ShareIntentParser.parse(
            action = ShareIntentParser.ACTION_SEND,
            mimeType = "text/plain",
            subject = null,
            text = "https://example.com/article",
            uriStrings = emptyList(),
        )
        assertEquals("https://example.com/article", payload!!.text)
        assertEquals(emptyList<String>(), payload.imageUriStrings)
    }

    @Test
    fun `subject is joined ahead of the shared text`() {
        val payload = ShareIntentParser.parse(
            action = ShareIntentParser.ACTION_SEND,
            mimeType = "text/plain",
            subject = "Interesting read",
            text = "https://example.com/article",
            uriStrings = emptyList(),
        )
        assertEquals("Interesting read\n\nhttps://example.com/article", payload!!.text)
    }

    @Test
    fun `single image share yields the image uri`() {
        val payload = ShareIntentParser.parse(
            action = ShareIntentParser.ACTION_SEND,
            mimeType = "image/jpeg",
            subject = null,
            text = null,
            uriStrings = listOf("content://media/1"),
        )
        assertNull(payload!!.text)
        assertEquals(listOf("content://media/1"), payload.imageUriStrings)
    }

    @Test
    fun `multiple images are all captured`() {
        val payload = ShareIntentParser.parse(
            action = ShareIntentParser.ACTION_SEND_MULTIPLE,
            mimeType = "image/*",
            subject = null,
            text = null,
            uriStrings = listOf("content://media/1", "content://media/2"),
        )
        assertEquals(listOf("content://media/1", "content://media/2"), payload!!.imageUriStrings)
    }

    @Test
    fun `image share keeps accompanying text`() {
        val payload = ShareIntentParser.parse(
            action = ShareIntentParser.ACTION_SEND,
            mimeType = "image/png",
            subject = null,
            text = "look at this",
            uriStrings = listOf("content://media/9"),
        )
        assertEquals("look at this", payload!!.text)
        assertEquals(listOf("content://media/9"), payload.imageUriStrings)
    }

    @Test
    fun `non-share actions return null`() {
        assertNull(
            ShareIntentParser.parse(
                action = "android.intent.action.VIEW",
                mimeType = "text/plain",
                subject = null,
                text = "hello",
                uriStrings = emptyList(),
            ),
        )
        assertNull(
            ShareIntentParser.parse(
                action = null,
                mimeType = "text/plain",
                subject = null,
                text = "hello",
                uriStrings = emptyList(),
            ),
        )
    }

    @Test
    fun `unsupported mime types return null`() {
        assertNull(
            ShareIntentParser.parse(
                action = ShareIntentParser.ACTION_SEND,
                mimeType = "application/pdf",
                subject = null,
                text = null,
                uriStrings = listOf("content://media/1"),
            ),
        )
    }

    @Test
    fun `share with no usable content returns null`() {
        assertNull(
            ShareIntentParser.parse(
                action = ShareIntentParser.ACTION_SEND,
                mimeType = "text/plain",
                subject = null,
                text = "   ",
                uriStrings = emptyList(),
            ),
        )
        assertNull(
            ShareIntentParser.parse(
                action = ShareIntentParser.ACTION_SEND,
                mimeType = "image/jpeg",
                subject = null,
                text = null,
                uriStrings = listOf("  "),
            ),
        )
    }
}
