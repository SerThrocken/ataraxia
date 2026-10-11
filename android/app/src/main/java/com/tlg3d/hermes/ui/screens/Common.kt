package com.tlg3d.hermes.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import java.time.Instant
import java.time.ZoneId

/** Screen title with heading semantics, used by the tab screens. */
@Composable
fun ScreenTitle(title: String) {
    Text(
        title,
        style = MaterialTheme.typography.headlineSmall,
        modifier = Modifier.fillMaxWidth()
            .padding(horizontal = 16.dp, vertical = 12.dp)
            .semantics { heading() },
    )
}

/** Centered spinner used while a tab's first load is in flight. */
@Composable
fun LoadingState(label: String = "Loading…") {
    Box(
        Modifier.fillMaxSize().semantics { contentDescription = label },
        contentAlignment = Alignment.Center,
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            CircularProgressIndicator()
            Spacer(Modifier.height(12.dp))
            Text(label, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

/** Empty-list state: honest title + body, never placeholder copy. */
@Composable
fun EmptyState(title: String, body: String) {
    Box(
        Modifier.fillMaxSize().padding(32.dp)
            .semantics { contentDescription = "$title. $body" },
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(
                title,
                style = MaterialTheme.typography.titleMedium,
                textAlign = TextAlign.Center,
            )
            Text(
                body,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
        }
    }
}

/** Dismissible error strip, shared by the tab screens. */
@Composable
fun ErrorBar(message: String, onDismiss: () -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp)
            .semantics { contentDescription = "Error: $message" },
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.errorContainer,
        ),
    ) {
        Row(
            Modifier.padding(12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                message,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onErrorContainer,
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = onDismiss) { Text("Dismiss") }
        }
    }
}

/**
 * Human relative time for gateway epoch-second timestamps
 * ([com.tlg3d.hermes.network.Session.lastActive]/`startedAt`).
 */
fun relativeTime(epochSeconds: Double?): String {
    if (epochSeconds == null || epochSeconds <= 0) return "unknown time"
    val then = epochSeconds.toLong()
    val delta = System.currentTimeMillis() / 1000 - then
    if (delta < 0) return "just now"
    return when {
        delta < 60 -> "just now"
        delta < 3600 -> "${delta / 60}m ago"
        delta < 86_400 -> "${delta / 3600}h ago"
        delta < 86_400 * 7 -> "${delta / 86_400}d ago"
        else -> Instant.ofEpochSecond(then).atZone(ZoneId.systemDefault())
            .toLocalDate().toString()
    }
}

/** One-line error classifier shared by the tab view models. */
fun describeGatewayError(e: Exception): String = when (e) {
    is com.tlg3d.hermes.network.GatewayException -> e.message ?: "Gateway error."
    else -> e.message ?: e.javaClass.simpleName
}
