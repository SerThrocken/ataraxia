package com.tlg3d.hermes.share

/**
 * Pure parser for `ACTION_SEND` / `ACTION_SEND_MULTIPLE` intents. Takes only
 * primitives (no Android `Intent`) so it runs on the JVM under unit tests.
 *
 * Supported: text MIME types (text/plain and friends) and image MIME types,
 * single and multiple. Anything else (PDFs, videos, …) returns null — the
 * caller shows the normal launcher instead of a half-working prefill.
 */
object ShareIntentParser {
    const val ACTION_SEND = "android.intent.action.SEND"
    const val ACTION_SEND_MULTIPLE = "android.intent.action.SEND_MULTIPLE"

    fun parse(
        action: String?,
        mimeType: String?,
        subject: String?,
        text: String?,
        uriStrings: List<String>,
    ): SharePayload? {
        if (action != ACTION_SEND && action != ACTION_SEND_MULTIPLE) return null
        val type = mimeType.orEmpty()
        if (!type.startsWith("text/") && !type.startsWith("image/")) return null

        val sharedText = joinText(subject, text)
        val images = uriStrings.map { it.trim() }.filter { it.isNotEmpty() }
        if (sharedText == null && images.isEmpty()) return null
        return SharePayload(sharedText, images)
    }

    private fun joinText(subject: String?, text: String?): String? =
        listOfNotNull(subject?.trim()?.takeIf { it.isNotEmpty() }, text?.trim()?.takeIf { it.isNotEmpty() })
            .joinToString("\n\n")
            .takeIf { it.isNotEmpty() }
}
