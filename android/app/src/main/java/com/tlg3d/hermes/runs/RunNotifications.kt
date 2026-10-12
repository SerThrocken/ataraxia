package com.tlg3d.hermes.runs

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import androidx.core.app.NotificationCompat
import com.tlg3d.hermes.MainActivity
import com.tlg3d.hermes.network.RunStatus

/**
 * Builder for the persistent run-control notification.
 *
 * Privacy: the content carries only the run id and its status label — never
 * the API key or any message content. Lock-screen visibility is PUBLIC so
 * Stop/Steer work from the lock screen; that is safe exactly because the
 * content holds no secrets.
 *
 * No Nudge action: the gateway exposes no nudge endpoint (verified in
 * gateway/platforms/api_server_runs.py `_http_routes`, 2026-10-12 — only
 * approval, steer, stop). Guidance goes through Steer.
 *
 * Accessibility: NotificationCompat actions expose no contentDescription
 * API — TalkBack announces the action titles, so they are full verb phrases
 * ("Stop run", "Steer run") rather than bare verbs.
 */
object RunNotifications {
    const val CHANNEL_ID = "ataraxia_run_controls"
    const val NOTIFICATION_ID = 4201

    fun ensureChannel(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java)
        if (manager.getNotificationChannel(CHANNEL_ID) != null) return
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                "Agent run controls",
                NotificationManager.IMPORTANCE_DEFAULT,
            ).apply {
                description = "Persistent Stop / Steer controls for the active agent run."
            },
        )
    }

    fun build(context: Context, runId: String, status: RunStatus?): NotificationCompat.Builder {
        val label = status?.let { runStateLabel(it.status) } ?: "Starting…"
        val detail = status?.lastEvent?.takeIf { it.isNotBlank() }?.let { " · $it" }.orEmpty()
        val stop = PendingIntent.getForegroundService(
            context,
            runId.hashCode(),
            RunControlService.intent(context, runId, RunControlService.COMMAND_STOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val steer = MainActivity.steerPendingIntent(context, runId)
        return NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle("Agent run ${shortRunId(runId)}")
            .setContentText("$label$detail")
            .setOngoing(true)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setContentIntent(steer)
            .addAction(android.R.drawable.ic_menu_close_clear_cancel, "Stop run", stop)
            .addAction(android.R.drawable.ic_menu_edit, "Steer run", steer)
    }
}
