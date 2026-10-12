package com.tlg3d.hermes.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import com.tlg3d.hermes.HermesApp
import com.tlg3d.hermes.runs.runStateLabel
import com.tlg3d.hermes.runs.shortRunId
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Steer input for one active agent run. Reached from the persistent
 * notification's "Steer run" action (deep link) — or any navigation to
 * `steer/{runId}`. Sends `POST /v1/runs/{id}/steer {"message"}`; the gateway
 * 409s when the run isn't `running`, and that error is shown verbatim.
 */
@Composable
fun SteerScreen(runId: String) {
    val app = LocalContext.current.applicationContext as HermesApp
    val scope = rememberCoroutineScope()
    var guidance by remember { mutableStateOf("") }
    var runState by remember { mutableStateOf<String?>(null) }
    var result by remember { mutableStateOf<String?>(null) }
    var sending by remember { mutableStateOf(false) }

    LaunchedEffect(runId) {
        runState = try {
            val status = withContext(Dispatchers.IO) { app.gatewayClient()?.getRun(runId) }
            status?.let { runStateLabel(it.status) } ?: "No API key saved"
        } catch (e: Exception) {
            "Couldn't load run: ${describeGatewayError(e)}"
        }
    }

    fun send() {
        val text = guidance.trim()
        if (text.isEmpty() || sending) return
        sending = true
        result = null
        scope.launch(Dispatchers.IO) {
            val outcome = try {
                val client = app.gatewayClient()
                    ?: return@launch withContext(Dispatchers.Main) {
                        result = "No API key saved."
                        sending = false
                    }
                if (client.steerRun(runId, text)) "Guidance sent — the run acknowledged it."
                else "The gateway did not accept the steer."
            } catch (e: Exception) {
                describeGatewayError(e)
            }
            withContext(Dispatchers.Main) {
                result = outcome
                sending = false
                if (outcome.startsWith("Guidance sent")) guidance = ""
            }
        }
    }

    Column(
        Modifier.fillMaxSize().padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        ScreenTitle("Steer run")
        Text(
            "Run ${shortRunId(runId)} · ${runState ?: "loading…"}",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.semantics { contentDescription = "Run status: ${runState ?: "loading"}" },
        )
        OutlinedTextField(
            value = guidance,
            onValueChange = { guidance = it },
            label = { Text("Guidance for the agent") },
            modifier = Modifier.fillMaxWidth()
                .semantics { contentDescription = "Guidance text for the run" },
            minLines = 3,
        )
        Button(
            onClick = ::send,
            enabled = !sending && guidance.isNotBlank(),
            modifier = Modifier.semantics { contentDescription = "Send guidance to run" },
        ) {
            Text(if (sending) "Sending…" else "Send guidance")
        }
        result?.let {
            Spacer(Modifier.height(4.dp))
            Text(
                it,
                style = MaterialTheme.typography.bodyMedium,
                color = if (it.startsWith("Guidance sent")) MaterialTheme.colorScheme.primary
                else MaterialTheme.colorScheme.error,
                modifier = Modifier.semantics { contentDescription = "Steer result: $it" },
            )
        }
    }
}
