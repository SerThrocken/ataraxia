package com.tlg3d.hermes.widget

import android.content.Context
import androidx.compose.ui.graphics.Color
import androidx.glance.Button
import androidx.glance.GlanceId
import androidx.glance.GlanceModifier
import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.action.actionRunCallback
import androidx.glance.appwidget.provideContent
import androidx.glance.background
import androidx.glance.color.ColorProvider
import androidx.glance.layout.Column
import androidx.glance.layout.fillMaxSize
import androidx.glance.layout.padding
import androidx.glance.text.FontWeight
import androidx.glance.text.Text
import androidx.glance.text.TextStyle
import com.tlg3d.hermes.HermesApp
import com.tlg3d.hermes.data.GatewayPrefs
import com.tlg3d.hermes.network.GatewayConfig
import com.tlg3d.hermes.network.OkHttpGatewayClient
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Home-screen gateway-status widget (Glance 1.1.1).
 *
 * Shows the gateway connection state, the `GET /health` round-trip latency,
 * and the last successful ping time. The health endpoint needs no auth, so
 * the widget pings even before an API key is saved — in that case it shows
 * the honest "No API key saved" empty state instead of crashing.
 *
 * Refresh: WorkManager periodic (15 min, the Android minimum) scheduled in
 * [HermesApp.onCreate], plus the manual Refresh button below.
 *
 * Colors are the Ataraxia dark palette (Theme.kt); widgets don't follow the
 * app theme, and dark is the primary design target. Layout is intentionally
 * plain: Glance 1.1.1 exposes no dp/weight modifiers, so no padding tricks.
 */
class GatewayStatusWidget : GlanceAppWidget() {

    override suspend fun provideGlance(context: Context, id: GlanceId) {
        val state = loadState(context)
        provideContent {
            Column(
                modifier = GlanceModifier.fillMaxSize()
                    .background(ColorProvider(day = Slate10, night = Slate10))
                    .padding(16),
            ) {
                Text(
                    text = "Ataraxia · gateway",
                    style = TextStyle(
                        color = ColorProvider(day = Teal80, night = Teal80),
                        fontWeight = FontWeight.Medium,
                    ),
                )
                Text(
                    text = widgetStatusLine(state),
                    style = TextStyle(
                        color = ColorProvider(day = Slate95, night = Slate95),
                        fontWeight = FontWeight.Bold,
                    ),
                )
                Text(
                    text = widgetDetailLine(state),
                    style = TextStyle(color = ColorProvider(day = Slate80, night = Slate80)),
                )
                Button(
                    text = "Refresh",
                    onClick = actionRunCallback<RefreshWidgetAction>(),
                )
            }
        }
    }

    private suspend fun loadState(context: Context): WidgetState {
        val app = context.applicationContext as HermesApp
        val key = app.secretStore.getApiKey(GatewayPrefs.API_KEY_ALIAS)
        val client = OkHttpGatewayClient(GatewayConfig.forHost(app.gatewayPrefs.host, key.orEmpty()))
        val prefs = context.getSharedPreferences(WIDGET_PREFS, Context.MODE_PRIVATE)
        val latency = client.healthLatencyMs()
        if (latency != null) {
            prefs.edit().putLong(KEY_LAST_SEEN, System.currentTimeMillis()).apply()
        }
        val lastSeen = prefs.getLong(KEY_LAST_SEEN, 0).takeIf { it > 0 }
            ?.let { TIME_FORMAT.format(Date(it)) }
        return WidgetState(
            hasKey = !key.isNullOrBlank(),
            connected = latency != null,
            latencyMs = latency,
            lastSeenLabel = lastSeen,
        )
    }

    companion object {
        const val WIDGET_PREFS = "ataraxia_widget"
        const val KEY_LAST_SEEN = "last_seen_ms"

        private val TIME_FORMAT = SimpleDateFormat("HH:mm", Locale.getDefault())

        // Ataraxia dark palette (ui/theme/Theme.kt).
        private val Slate10 = Color(0xFF0D1417)
        private val Slate95 = Color(0xFFDCE7E3)
        private val Slate80 = Color(0xFF9DB3AC)
        private val Teal80 = Color(0xFF7FD8C2)
    }
}
