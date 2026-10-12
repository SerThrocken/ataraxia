package com.tlg3d.hermes.runs

/**
 * Pure display mappings for agent runs. No Android, no I/O — JVM-testable.
 *
 * Status labels cover exactly the status set the gateway emits
 * (RUNS-API.md); anything else passes through unchanged rather than
 * inventing a label.
 */
fun shortRunId(id: String): String =
    if (id.length <= 14) id else id.take(12) + "…"

fun runStateLabel(status: String): String = when (status) {
    "queued" -> "Queued"
    "running" -> "Running"
    "waiting_for_approval" -> "Waiting for approval"
    "stopping" -> "Stopping"
    "completed" -> "Completed"
    "failed" -> "Failed"
    "cancelled" -> "Cancelled"
    else -> status
}
