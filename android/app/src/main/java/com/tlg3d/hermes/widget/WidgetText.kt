package com.tlg3d.hermes.widget

/**
 * Pure text mappings for the gateway-status widget. No Android, no I/O —
 * JVM-testable. The widget never invents state: no API key, failed ping,
 * and successful ping each get their own honest line.
 */
data class WidgetState(
    val hasKey: Boolean,
    val connected: Boolean,
    val latencyMs: Long?,
    /** Pre-formatted local clock time of the last successful ping, if any. */
    val lastSeenLabel: String?,
)

fun widgetStatusLine(state: WidgetState): String = when {
    !state.hasKey -> "No API key saved"
    state.connected -> "Connected"
    else -> "Gateway unreachable"
}

fun widgetDetailLine(state: WidgetState): String = when {
    !state.hasKey -> "Open Ataraxia → Settings to add your key"
    state.connected -> buildString {
        state.latencyMs?.let { append("$it ms") }
        state.lastSeenLabel?.let {
            if (isNotEmpty()) append(" · ")
            append("last seen $it")
        }
    }.ifEmpty { "OK" }
    else -> state.lastSeenLabel?.let { "Last seen $it" } ?: "No successful ping yet"
}
