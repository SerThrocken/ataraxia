package com.tlg3d.hermes.ui.screens

import android.Manifest
import android.app.Application
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.FilledIconButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewmodel.compose.viewModel
import com.tlg3d.hermes.HermesApp
import com.tlg3d.hermes.voice.VoiceLoopState
import com.tlg3d.hermes.voice.VoiceViewModel

private fun statusText(state: VoiceLoopState): String = when (state) {
    VoiceLoopState.Idle -> "Tap the microphone to start a hands-free conversation."
    VoiceLoopState.Listening -> "Listening… speak now."
    VoiceLoopState.Thinking -> "Thinking…"
    VoiceLoopState.Speaking -> "Speaking…"
    is VoiceLoopState.Error -> state.message
}

@Composable
fun VoiceScreen() {
    val app = LocalContext.current.applicationContext as HermesApp
    val context = LocalContext.current
    val vm: VoiceViewModel = viewModel(
        factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                VoiceViewModel(app) as T
        },
    )

    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted -> if (granted) vm.startLoop() }

    fun ensureMicThenStart() {
        val granted = ContextCompat.checkSelfPermission(
            context, Manifest.permission.RECORD_AUDIO,
        ) == PackageManager.PERMISSION_GRANTED
        if (granted) vm.startLoop()
        else permissionLauncher.launch(Manifest.permission.RECORD_AUDIO)
    }

    val active = vm.state !is VoiceLoopState.Idle && vm.state !is VoiceLoopState.Error

    Column(Modifier.fillMaxSize()) {
        ScreenTitle("Voice")
        VoiceNoticeCard(vm)
        Spacer(Modifier.height(8.dp))
        Column(
            Modifier.fillMaxWidth().padding(horizontal = 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            FilledIconButton(
                onClick = { if (active) vm.stopLoop() else ensureMicThenStart() },
                modifier = Modifier.size(96.dp)
                    .semantics {
                        contentDescription =
                            if (active) "Stop voice conversation" else "Start voice conversation"
                    },
                shape = CircleShape,
                colors = IconButtonDefaults.filledIconButtonColors(
                    containerColor = if (active) MaterialTheme.colorScheme.errorContainer
                    else MaterialTheme.colorScheme.primary,
                ),
            ) {
                Icon(
                    if (active) Icons.Filled.Stop else Icons.Filled.Mic,
                    contentDescription = null,
                    modifier = Modifier.size(40.dp),
                )
            }
            Spacer(Modifier.height(12.dp))
            Text(
                statusText(vm.state),
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier.semantics {
                    contentDescription = "Voice status: ${statusText(vm.state)}"
                },
            )
        }
        Spacer(Modifier.height(8.dp))
        LazyColumn(
            modifier = Modifier.weight(1f).fillMaxWidth()
                .semantics { contentDescription = "Voice transcript" },
            verticalArrangement = Arrangement.spacedBy(8.dp),
            contentPadding = androidx.compose.foundation.layout.PaddingValues(12.dp),
        ) {
            itemsIndexed(vm.transcript, key = { index, _ -> index }) { _, turn ->
                Column(
                    Modifier.fillMaxWidth()
                        .semantics { contentDescription = "${turn.speaker} said: ${turn.text}" },
                ) {
                    Text(
                        turn.speaker,
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.semantics { heading() },
                    )
                    Text(turn.text, style = MaterialTheme.typography.bodyLarge)
                }
            }
        }
    }
}

/**
 * The honest bit: the gateway exposes no voice API, so recognition and
 * synthesis run on-device via Android platform APIs. Turn-based, not
 * realtime; network recognition is possible when the device has no
 * on-device engine.
 */
@Composable
private fun VoiceNoticeCard(vm: VoiceViewModel) {
    Card(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp)
            .semantics { contentDescription = "About voice mode" },
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceContainerLow,
        ),
    ) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text(
                "Hands-free loop: speak, the agent replies out loud. Turn-based — not realtime voice.",
                style = MaterialTheme.typography.bodySmall,
            )
            Text(
                "Speech recognition and synthesis run on-device (Android platform APIs). " +
                    "The gateway reports audio_api=false / realtime_voice=false " +
                    "(GET /v1/capabilities) — there is no server voice endpoint. " +
                    "Recognition may use the network when this device has no on-device engine.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (!vm.recognitionAvailable) {
                Text(
                    "Speech recognition is not available on this device.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.error,
                )
            }
            vm.notice?.let {
                Text(
                    it,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.error,
                )
            }
        }
    }
}
