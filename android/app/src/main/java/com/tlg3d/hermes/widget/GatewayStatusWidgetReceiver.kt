package com.tlg3d.hermes.widget

import androidx.glance.appwidget.GlanceAppWidget
import androidx.glance.appwidget.GlanceAppWidgetReceiver

/** Host entry point for the gateway-status widget (see the manifest). */
class GatewayStatusWidgetReceiver : GlanceAppWidgetReceiver() {
    override val glanceAppWidget: GlanceAppWidget = GatewayStatusWidget()
}
