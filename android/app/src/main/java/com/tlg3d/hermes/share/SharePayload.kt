package com.tlg3d.hermes.share

/**
 * Content handed to Ataraxia by another app's share sheet. Image URIs are
 * plain strings (not `android.net.Uri`) so the parsing logic stays pure and
 * JVM-testable; the UI layer re-parses them where a `ContentResolver` exists.
 */
data class SharePayload(
    val text: String?,
    val imageUriStrings: List<String>,
)
