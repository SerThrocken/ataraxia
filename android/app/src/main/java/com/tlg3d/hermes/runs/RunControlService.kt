package com.tlg3d.hermes.runs

import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.IBinder
import com.tlg3d.hermes.HermesApp
import com.tlg3d.hermes.network.GatewayException
import com.tlg3d.hermes.network.RunStatus
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlin.coroutines.coroutineContext

/**
 * Foreground service behind the persistent run-control notification.
 *
 * Lifecycle: started when the client learns of an active run ([trackActiveRun]
 * from chat-stream SSE, or the notification's own Stop action). Polls
 * `GET /v1/runs/{id}` with backoff (2s, doubling, cap 30s — the RUNS-API.md
 * guidance), refreshes the notification, and stops itself — clearing the
 * notification — once the run reaches a terminal status. A 404/409 from the
 * gateway (forgotten run, or stop raced a natural finish) also ends tracking.
 *
 * The Stop action re-enters with [COMMAND_STOP]: the service issues
 * `POST /v1/runs/{id}/stop` once, then keeps polling until the run reports
 * terminal. The PendingIntent uses `getForegroundService`, which is the
 * platform-sanctioned way to start an FGS from a notification action while
 * the app is in the background.
 */
class RunControlService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var monitorJob: Job? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val runId = intent?.getStringExtra(EXTRA_RUN_ID)
            ?: ActiveRunStore(this).activeRunId
        if (runId.isNullOrBlank()) {
            stopSelf()
            return START_NOT_STICKY
        }
        RunNotifications.ensureChannel(this)
        startForeground(
            RunNotifications.NOTIFICATION_ID,
            RunNotifications.build(this, runId, null).build(),
        )
        monitorJob?.cancel()
        val stopFirst = intent?.getStringExtra(EXTRA_COMMAND) == COMMAND_STOP
        monitorJob = scope.launch { monitor(runId, stopFirst) }
        return START_NOT_STICKY
    }

    private suspend fun monitor(runId: String, stopFirst: Boolean) {
        val app = applicationContext as HermesApp
        val client = app.gatewayClient()
        if (client == null) {
            // No API key saved: honest stop, no crash, no notification left behind.
            stopSelf()
            return
        }
        try {
            if (stopFirst) {
                try {
                    client.stopRun(runId)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    // Stop raced a natural finish (409) or the gateway flapped;
                    // the poll loop below settles the real state.
                }
            }
            // Backoff per RUNS-API.md: 2s, doubling on unchanged status, cap 30s.
            var delayMs = 2_000L
            var lastStatus: String? = null
            while (coroutineContext.isActive) {
                val status: RunStatus? = try {
                    client.getRun(runId)
                } catch (e: CancellationException) {
                    throw e
                } catch (e: GatewayException) {
                    // Forgotten run (404) or stop raced completion (409):
                    // tracking ends. Anything else is transient.
                    if (e.statusCode == 404 || e.statusCode == 409) break else null
                } catch (e: Exception) {
                    null // transient: keep the last known state, back off, retry
                }
                if (status != null) {
                    refreshNotification(runId, status)
                    delayMs = if (status.status == lastStatus) {
                        minOf(delayMs * 2, 30_000)
                    } else {
                        2_000L
                    }
                    lastStatus = status.status
                    if (status.isTerminal) break
                } else {
                    delayMs = minOf(delayMs * 2, 30_000)
                }
                delay(delayMs)
            }
        } finally {
            ActiveRunStore(this).clearIf(runId)
            stopSelf()
        }
    }

    private fun refreshNotification(runId: String, status: RunStatus) {
        getSystemService(NotificationManager::class.java).notify(
            RunNotifications.NOTIFICATION_ID,
            RunNotifications.build(this, runId, status).build(),
        )
    }

    override fun onDestroy() {
        monitorJob?.cancel()
        scope.cancel()
        super.onDestroy()
    }

    companion object {
        const val EXTRA_RUN_ID = "run_id"
        const val EXTRA_COMMAND = "command"
        const val COMMAND_STOP = "stop"

        fun intent(context: Context, runId: String, command: String? = null): Intent =
            Intent(context, RunControlService::class.java).apply {
                putExtra(EXTRA_RUN_ID, runId)
                command?.let { putExtra(EXTRA_COMMAND, it) }
            }
    }
}
