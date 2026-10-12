package com.tlg3d.hermes.runs

import android.content.Context
import androidx.core.content.ContextCompat

/**
 * The one run id this client is currently tracking. The gateway exposes no
 * run list (GET /v1/runs is 405, RUNS-API.md), so the client tracks runs it
 * learns about — currently from `approval.request` SSE events in chat
 * streams, which carry the run id.
 */
class ActiveRunStore(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    var activeRunId: String?
        get() = prefs.getString(KEY_RUN_ID, null)
        private set(value) {
            prefs.edit().putString(KEY_RUN_ID, value).apply()
        }

    fun setActiveRun(runId: String) {
        activeRunId = runId
    }

    fun clearIf(runId: String) {
        if (activeRunId == runId) activeRunId = null
    }

    companion object {
        const val PREFS_NAME = "ataraxia_runs"
        const val KEY_RUN_ID = "active_run_id"
    }
}

/**
 * Registers a run the gateway told us about and raises its persistent
 * controls ([RunControlService]). No-op for blank ids. Safe to call from
 * the UI thread; the service does its work on IO.
 */
fun trackActiveRun(context: Context, runId: String) {
    if (runId.isBlank()) return
    ActiveRunStore(context).setActiveRun(runId)
    ContextCompat.startForegroundService(context, RunControlService.intent(context, runId))
}
