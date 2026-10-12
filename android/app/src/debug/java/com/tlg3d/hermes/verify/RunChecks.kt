package com.tlg3d.hermes.verify

import com.tlg3d.hermes.network.HermesJson
import com.tlg3d.hermes.network.RunStatus
import com.tlg3d.hermes.network.SteerResponse
import com.tlg3d.hermes.runs.runStateLabel
import com.tlg3d.hermes.runs.shortRunId
import com.tlg3d.hermes.widget.WidgetState
import com.tlg3d.hermes.widget.widgetDetailLine
import com.tlg3d.hermes.widget.widgetStatusLine
import kotlinx.serialization.decodeFromString

/**
 * Offline checks for the Worker D surface (run controls + gateway widget).
 *
 * Runs on the JVM via the `verifyRunChecks` Gradle task — same zero-dependency
 * pattern as `verifyGatewayLive`. JUnit 4/5 are EPL-licensed and fail the
 * project's MIT/Apache-2.0/BSD/ISC license gate, so these use plain Kotlin
 * `check()` assertions with an exact pass count instead.
 *
 * Every check asserts a gateway-verified contract (RUNS-API.md, 2026-10-11)
 * or a pure display mapping. Nothing here touches the network or Android.
 */
object RunChecks {
    private var passed = 0

    private fun check(name: String, cond: Boolean) {
        check(cond) { "FAILED: $name" }
        passed++
        println("  ok - $name")
    }

    fun runAll() {
        println("RunChecks: offline contract + display checks")

        // 1. Full completed-run payload (shape mirrors windows Dtos.Runs).
        val completed = HermesJson.decodeFromString<RunStatus>(
            """{"object":"hermes.run","run_id":"run_abc123","status":"completed",
               |"created_at":1760246400.0,"updated_at":1760246460.5,"session_id":"sess_1",
               |"model":"hermes-agent","last_event":"run.completed","output":"done",
               |"usage":{"input_tokens":10,"output_tokens":20,"total_tokens":30,
               |"cache_read_tokens":1,"cache_write_tokens":2},
               |"runtime":{"provider":"x","model":"m","route_source":"chat-smart"},
               |"completed":true,"partial":false,"interrupted":false}""".trimMargin(),
        )
        check("completed payload: id", completed.runId == "run_abc123")
        check("completed payload: terminal", completed.isTerminal)
        check("completed payload: usage", completed.usage?.totalTokens == 30L)
        check("completed payload: runtime", completed.runtime?.provider == "x")

        // 2. Minimal stopping payload (POST /v1/runs/{id}/stop while active).
        val stopping = HermesJson.decodeFromString<RunStatus>(
            """{"run_id":"run_abc123","status":"stopping"}""",
        )
        check("stopping payload: not terminal", !stopping.isTerminal)
        check("stopping payload: defaults", stopping.output == null && stopping.usage == null)

        // 3. Terminal statuses.
        listOf("completed", "failed", "cancelled").forEach { s ->
            check(
                "terminal: $s",
                HermesJson.decodeFromString<RunStatus>(
                    """{"run_id":"r","status":"$s"}""",
                ).isTerminal,
            )
        }

        // 4. Non-terminal statuses.
        listOf("queued", "running", "waiting_for_approval", "stopping").forEach { s ->
            check(
                "non-terminal: $s",
                !HermesJson.decodeFromString<RunStatus>(
                    """{"run_id":"r","status":"$s"}""",
                ).isTerminal,
            )
        }

        // 5. Unknown keys are ignored (additive server changes don't break us).
        val extra = HermesJson.decodeFromString<RunStatus>(
            """{"run_id":"r","status":"running","future_field":{"n":1}}""",
        )
        check("unknown keys ignored", extra.status == "running")

        // 6. Steer response shape: {"object":"hermes.run.steer","run_id","accepted"}.
        val steer = HermesJson.decodeFromString<SteerResponse>(
            """{"object":"hermes.run.steer","run_id":"run_x","accepted":true}""",
        )
        check("steer accepted", steer.accepted && steer.runId == "run_x")
        val steerMissing = HermesJson.decodeFromString<SteerResponse>(
            """{"run_id":"run_x"}""",
        )
        check("steer missing accepted defaults false", !steerMissing.accepted)

        // 7. shortRunId.
        check("shortRunId truncates", shortRunId("run_abcdef1234567890") == "run_abcdef12…")
        check("shortRunId keeps short ids", shortRunId("run_abc") == "run_abc")

        // 8. runStateLabel maps every gateway status (RUNS-API.md status set).
        check("label running", runStateLabel("running") == "Running")
        check("label queued", runStateLabel("queued") == "Queued")
        check(
            "label waiting_for_approval",
            runStateLabel("waiting_for_approval") == "Waiting for approval",
        )
        check("label stopping", runStateLabel("stopping") == "Stopping")
        check("label completed", runStateLabel("completed") == "Completed")
        check("label failed", runStateLabel("failed") == "Failed")
        check("label cancelled", runStateLabel("cancelled") == "Cancelled")

        // 9. Unknown statuses pass through unchanged (no invented labels).
        check("label unknown passthrough", runStateLabel("weird_status") == "weird_status")

        // 10. Widget status line.
        check(
            "widget: no key",
            widgetStatusLine(WidgetState(hasKey = false, connected = false, latencyMs = null, lastSeenLabel = null)) == "No API key saved",
        )
        check(
            "widget: connected",
            widgetStatusLine(WidgetState(hasKey = true, connected = true, latencyMs = 42, lastSeenLabel = "04:12")) == "Connected",
        )
        check(
            "widget: unreachable",
            widgetStatusLine(WidgetState(hasKey = true, connected = false, latencyMs = null, lastSeenLabel = null)) == "Gateway unreachable",
        )

        // 11. Widget detail line.
        check(
            "widget detail: no key guides to Settings",
            widgetDetailLine(WidgetState(hasKey = false, connected = false, latencyMs = null, lastSeenLabel = null))
                .contains("Settings"),
        )
        check(
            "widget detail: connected shows latency + last seen",
            widgetDetailLine(WidgetState(hasKey = true, connected = true, latencyMs = 42, lastSeenLabel = "04:12")) == "42 ms · last seen 04:12",
        )
        check(
            "widget detail: unreachable with history",
            widgetDetailLine(WidgetState(hasKey = true, connected = false, latencyMs = null, lastSeenLabel = "04:12")) == "Last seen 04:12",
        )
        check(
            "widget detail: unreachable, never seen",
            widgetDetailLine(WidgetState(hasKey = true, connected = false, latencyMs = null, lastSeenLabel = null)) == "No successful ping yet",
        )

        println("RunChecks: $passed checks passed")
    }
}

fun main() = RunChecks.runAll()
