package com.tlg3d.hermes

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.Parcelable
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.material3.windowsizeclass.ExperimentalMaterial3WindowSizeClassApi
import androidx.compose.material3.windowsizeclass.calculateWindowSizeClass
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.tlg3d.hermes.share.ShareIntentParser
import com.tlg3d.hermes.share.SharePayload
import com.tlg3d.hermes.ui.HermesNav
import com.tlg3d.hermes.ui.theme.AtaraxiaTheme

@OptIn(ExperimentalMaterial3WindowSizeClassApi::class)
class MainActivity : ComponentActivity() {

    /** Pending share-sheet payload, consumed once by the Chat screen. */
    private var sharedContent by mutableStateOf<SharePayload?>(null)

    /** Run id from the run notification's "Steer run" action, consumed once. */
    private var steerRunId by mutableStateOf<String?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        extractShare(intent)?.let { sharedContent = it }
        steerRunId = intent?.getStringExtra(EXTRA_STEER_RUN_ID)
        enableEdgeToEdge()
        setContent {
            AtaraxiaTheme {
                HermesNav(
                    windowSizeClass = calculateWindowSizeClass(this),
                    steerRunId = steerRunId,
                    onSteerConsumed = { steerRunId = null },
                    sharedContent = sharedContent,
                    onSharedConsumed = { sharedContent = null },
                )
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        // singleTop: shares and notification taps arriving while the app is
        // open land here instead of creating a new activity.
        extractShare(intent)?.let { sharedContent = it }
        intent.getStringExtra(EXTRA_STEER_RUN_ID)?.let { steerRunId = it }
    }

    /**
     * Pulls text/image URIs out of an `ACTION_SEND[_MULTIPLE]` intent.
     * Returns null for anything the share sheet can't handle (non-share
     * actions, unsupported MIME types, empty payloads).
     */
    private fun extractShare(intent: Intent?): SharePayload? {
        if (intent == null) return null
        @Suppress("DEPRECATION") // getParcelableArrayListExtra(String, Class) needs API 33
        val uris: List<String> = when (intent.action) {
            Intent.ACTION_SEND -> listOfNotNull(
                intent.getParcelableExtra<Parcelable>(Intent.EXTRA_STREAM)?.toString(),
            )
            Intent.ACTION_SEND_MULTIPLE -> intent
                .getParcelableArrayListExtra<Parcelable>(Intent.EXTRA_STREAM)
                ?.map { it.toString() }.orEmpty()
            else -> return null
        }
        return ShareIntentParser.parse(
            action = intent.action,
            mimeType = intent.type,
            subject = intent.getCharSequenceExtra(Intent.EXTRA_SUBJECT)?.toString(),
            text = intent.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString(),
            uriStrings = uris,
        )
    }

    companion object {
        /** Extra carrying the run id for the notification's "Steer run" action. */
        const val EXTRA_STEER_RUN_ID = "steer_run_id"

        /** Pending intent used by the run-control notification's steer action. */
        fun steerPendingIntent(context: Context, runId: String): PendingIntent {
            val intent = Intent(context, MainActivity::class.java).apply {
                putExtra(EXTRA_STEER_RUN_ID, runId)
            }
            return PendingIntent.getActivity(
                context,
                runId.hashCode(),
                intent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
        }
    }
}
