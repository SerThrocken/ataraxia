package com.tlg3d.hermes.voice

import android.app.Application
import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.tlg3d.hermes.HermesApp
import com.tlg3d.hermes.network.GatewayClient
import com.tlg3d.hermes.network.StreamEvent
import com.tlg3d.hermes.ui.screens.describeGatewayError
import java.util.Locale
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** One spoken exchange, shown in the voice transcript. */
data class VoiceTurn(val speaker: String, val text: String)

/**
 * Hands-free loop: on-device speech recognition -> gateway chat turn ->
 * on-device speech synthesis -> listen again.
 *
 * The gateway exposes NO voice API (`audio_api=false`, `realtime_voice=false`
 * on `GET /v1/capabilities`, verified against gateway source 2026-10-12), so
 * STT/TTS are Android platform APIs (`android.speech`, no new dependencies).
 * Recognition prefers on-device when the device offers it; otherwise Android
 * may use network recognition — the UI says so.
 */
class VoiceViewModel(app: Application) : AndroidViewModel(app) {

    var state by mutableStateOf<VoiceLoopState>(VoiceLoopState.Idle)
        private set
    var transcript = mutableStateListOf<VoiceTurn>()
        private set
    var notice by mutableStateOf<String?>(null)
        private set

    /** False when the device has no speech recognizer at all. */
    val recognitionAvailable: Boolean =
        SpeechRecognizer.isRecognitionAvailable(app)

    private val hermesApp = app as HermesApp
    private var client: GatewayClient? = null
    private var sessionId: String? = null
    private var loopActive = false

    private val recognizer: SpeechRecognizer? =
        if (recognitionAvailable) SpeechRecognizer.createSpeechRecognizer(app) else null

    private var tts: TextToSpeech? = null
    private var ttsReady = false
    private val mainHandler = Handler(Looper.getMainLooper())

    init {
        tts = TextToSpeech(app) { status ->
            if (status != TextToSpeech.SUCCESS) {
                notice = "Text-to-speech failed to start on this device."
                return@TextToSpeech
            }
            val langStatus = tts?.setLanguage(Locale.getDefault())
            if (langStatus == TextToSpeech.LANG_MISSING_DATA ||
                langStatus == TextToSpeech.LANG_NOT_SUPPORTED
            ) {
                notice = "No voice data for ${Locale.getDefault().displayLanguage} — " +
                    "install it in Android's text-to-speech settings."
            } else {
                ttsReady = true
            }
        }
        recognizer?.setRecognitionListener(object : RecognitionListener {
            override fun onResults(results: Bundle) {
                val text = results
                    .getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
                    ?.firstOrNull().orEmpty()
                onSpeechResult(text)
            }

            override fun onError(error: Int) {
                onSpeechError(describeRecognizerError(error))
            }

            override fun onReadyForSpeech(params: Bundle) = Unit
            override fun onBeginningOfSpeech() = Unit
            override fun onRmsChanged(rmsdB: Float) = Unit
            override fun onBufferReceived(buffer: ByteArray) = Unit
            override fun onEndOfSpeech() = Unit
            override fun onPartialResults(partialResults: Bundle) = Unit
            override fun onEvent(eventType: Int, params: Bundle) = Unit
        })
        tts?.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
            override fun onStart(utteranceId: String) = Unit
            override fun onDone(utteranceId: String) {
                // TTS callbacks arrive off the main thread; hop before touching state.
                mainHandler.post {
                    if (loopActive) {
                        state = reduceVoiceLoop(state, VoiceLoopEvent.TtsDone)
                        beginListening()
                    }
                }
            }

            override fun onError(utteranceId: String) {
                mainHandler.post {
                    loopActive = false
                    state = reduceVoiceLoop(state, VoiceLoopEvent.TtsError("Speech synthesis failed."))
                }
            }
        })
    }

    /** Starts the loop in a fresh gateway session. Needs `RECORD_AUDIO` granted. */
    fun startLoop() {
        val c = hermesApp.gatewayClient()
        if (c == null) {
            state = VoiceLoopState.Error("No API key saved — add it in Settings first.")
            return
        }
        if (!recognitionAvailable || recognizer == null) {
            state = VoiceLoopState.Error("Speech recognition is not available on this device.")
            return
        }
        client = c
        transcript.clear()
        viewModelScope.launch {
            try {
                val session = withContext(Dispatchers.IO) { c.createSession("Voice chat") }
                sessionId = session.id
                loopActive = true
                state = reduceVoiceLoop(state, VoiceLoopEvent.Start)
                beginListening()
            } catch (e: Exception) {
                state = VoiceLoopState.Error(describeGatewayError(e))
            }
        }
    }

    fun stopLoop() {
        loopActive = false
        recognizer?.cancel()
        tts?.stop()
        state = reduceVoiceLoop(state, VoiceLoopEvent.Stop)
    }

    private fun beginListening() {
        if (!loopActive) return
        val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(
                RecognizerIntent.EXTRA_LANGUAGE_MODEL,
                RecognizerIntent.LANGUAGE_MODEL_FREE_FORM,
            )
            putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true)
            putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1)
        }
        recognizer?.startListening(intent)
    }

    private fun onSpeechResult(text: String) {
        state = reduceVoiceLoop(state, VoiceLoopEvent.SpeechResult(text))
        if (text.isBlank()) {
            beginListening() // nothing heard; keep listening
            return
        }
        think(text)
    }

    private fun onSpeechError(message: String) {
        loopActive = false
        state = reduceVoiceLoop(state, VoiceLoopEvent.SpeechError(message))
    }

    private fun think(text: String) {
        val c = client
        val sid = sessionId
        if (c == null || sid == null) {
            loopActive = false
            state = VoiceLoopState.Error("Voice session was lost.")
            return
        }
        transcript.add(VoiceTurn("You", text))
        viewModelScope.launch(Dispatchers.IO) {
            val reply = StringBuilder()
            var failure: String? = null
            var approvalNeeded = false
            try {
                c.streamChat(sid, text) { event ->
                    when (event) {
                        is StreamEvent.Delta -> reply.append(event.text)
                        is StreamEvent.Completed -> reply.clear().append(event.content)
                        is StreamEvent.Approval -> approvalNeeded = true
                        is StreamEvent.Failed -> failure = event.message
                        is StreamEvent.Reasoning -> Unit
                    }
                }
            } catch (e: Exception) {
                failure = describeGatewayError(e)
            }
            withContext(Dispatchers.Main) {
                val failureMsg: String? = failure
                when {
                    failureMsg != null -> {
                        loopActive = false
                        state = VoiceLoopState.Error(failureMsg)
                    }
                    approvalNeeded -> {
                        // Approvals need a tap; voice mode can't resolve them.
                        loopActive = false
                        state = VoiceLoopState.Error(
                            "The agent needs an approval — resolve it in the Chat tab.",
                        )
                    }
                    else -> {
                        val answer = reply.toString()
                        transcript.add(VoiceTurn("Ataraxia", answer))
                        state = reduceVoiceLoop(state, VoiceLoopEvent.ResponseComplete)
                        speak(answer)
                    }
                }
            }
        }
    }

    private fun speak(text: String) {
        if (text.isBlank()) {
            // Nothing to say; go straight back to listening.
            state = reduceVoiceLoop(state, VoiceLoopEvent.TtsDone)
            beginListening()
            return
        }
        if (!ttsReady) {
            loopActive = false
            state = reduceVoiceLoop(
                state,
                VoiceLoopEvent.TtsError("Text-to-speech is not ready on this device."),
            )
            return
        }
        tts?.speak(text, TextToSpeech.QUEUE_FLUSH, null, UTTERANCE_ID)
    }

    override fun onCleared() {
        loopActive = false
        recognizer?.destroy()
        tts?.shutdown()
        super.onCleared()
    }

    companion object {
        private const val UTTERANCE_ID = "ataraxia-voice-reply"

        private fun describeRecognizerError(error: Int): String = when (error) {
            SpeechRecognizer.ERROR_NO_MATCH -> "Didn't catch that — try again."
            SpeechRecognizer.ERROR_SPEECH_TIMEOUT -> "No speech heard."
            SpeechRecognizer.ERROR_NETWORK, SpeechRecognizer.ERROR_NETWORK_TIMEOUT ->
                "Recognition needs a network connection on this device right now."
            SpeechRecognizer.ERROR_INSUFFICIENT_PERMISSIONS ->
                "Microphone permission was denied."
            SpeechRecognizer.ERROR_RECOGNIZER_BUSY -> "The recognizer is busy — try again."
            else -> "Recognition failed (code $error)."
        }
    }
}
