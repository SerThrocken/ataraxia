package com.tlg3d.hermes.network

import java.util.Base64

/**
 * One image attached to a chat turn. Android-free (JVM-testable); Android
 * code decodes the bitmap and hands the encoded bytes here.
 *
 * Mirrors the Windows client's ChatImageAttachment: the gateway's
 * `_normalize_image_part` (api_server.py) accepts only `data:image/…;base64,…`
 * URLs or http(s) URLs — never file paths.
 */
class ChatImageAttachment(val data: ByteArray, val mimeType: String) {
    init {
        require(data.isNotEmpty()) { "Image data must not be empty." }
        require(mimeType.startsWith("image/", ignoreCase = true)) {
            "MIME type must be an image/* type."
        }
    }

    /** `data:image/…;base64,…` URL for the `image_url` content part. */
    fun toDataUrl(): String =
        "data:$mimeType;base64,${Base64.getEncoder().encodeToString(data)}"
}
