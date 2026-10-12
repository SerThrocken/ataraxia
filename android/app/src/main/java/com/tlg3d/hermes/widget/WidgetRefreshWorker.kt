package com.tlg3d.hermes.widget

import android.content.Context
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters

/**
 * Periodic widget refresh (15 min — the Android minimum for periodic work).
 * Re-renders every placed widget instance; each render re-pings /health.
 */
class WidgetRefreshWorker(
    appContext: Context,
    params: WorkerParameters,
) : CoroutineWorker(appContext, params) {

    override suspend fun doWork(): Result {
        val manager = GlanceAppWidgetManager(applicationContext)
        val widget = GatewayStatusWidget()
        manager.getGlanceIds(GatewayStatusWidget::class.java).forEach { id ->
            widget.update(applicationContext, id)
        }
        return Result.success()
    }
}
