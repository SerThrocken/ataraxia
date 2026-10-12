package com.tlg3d.hermes.voice

/**
 * Turn-based voice-loop states. Pure data — JVM-testable, no Android APIs.
 *
 * This is deliberately NOT realtime: the Hermes gateway reports
 * `audio_api=false` and `realtime_voice=false` (`GET /v1/capabilities`,
 * verified against gateway source 2026-10-12), so there is no server voice
 * endpoint. The loop sequences on-device speech recognition and synthesis
 * around ordinary `/chat/stream` turns: listen -> think -> speak -> listen.
 */
sealed interface VoiceLoopState {
    data object Idle : VoiceLoopState
    data object Listening : VoiceLoopState
    data object Thinking : VoiceLoopState
    data object Speaking : VoiceLoopState
    data class Error(val message: String) : VoiceLoopState
}

sealed interface VoiceLoopEvent {
    data object Start : VoiceLoopEvent
    data class SpeechResult(val text: String) : VoiceLoopEvent
    data class SpeechError(val message: String) : VoiceLoopEvent
    data object ResponseComplete : VoiceLoopEvent
    data object TtsDone : VoiceLoopEvent
    data class TtsError(val message: String) : VoiceLoopEvent
    data object Stop : VoiceLoopEvent
}

/**
 * Pure state transition. Stale or out-of-order events (e.g. a TTS callback
 * arriving while listening) leave the state unchanged instead of corrupting
 * the loop.
 */
fun reduceVoiceLoop(state: VoiceLoopState, event: VoiceLoopEvent): VoiceLoopState =
    when (event) {
        VoiceLoopEvent.Stop -> VoiceLoopState.Idle
        VoiceLoopEvent.Start -> when (state) {
            VoiceLoopState.Idle, is VoiceLoopState.Error -> VoiceLoopState.Listening
            else -> state
        }
        is VoiceLoopEvent.SpeechResult -> when (state) {
            VoiceLoopState.Listening ->
                if (event.text.isBlank()) state else VoiceLoopState.Thinking
            else -> state
        }
        is VoiceLoopEvent.SpeechError -> when (state) {
            VoiceLoopState.Listening -> VoiceLoopState.Error(event.message)
            else -> state
        }
        VoiceLoopEvent.ResponseComplete -> when (state) {
            VoiceLoopState.Thinking -> VoiceLoopState.Speaking
            else -> state
        }
        VoiceLoopEvent.TtsDone -> when (state) {
            VoiceLoopState.Speaking -> VoiceLoopState.Listening
            else -> state
        }
        is VoiceLoopEvent.TtsError -> when (state) {
            VoiceLoopState.Speaking -> VoiceLoopState.Error(event.message)
            else -> state
        }
    }
