package com.tlg3d.hermes.voice

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * State-machine tests for the turn-based voice loop. The loop is NOT realtime:
 * listen -> think -> speak -> listen again. The gateway exposes no voice
 * endpoint (audio_api=false), so this machine only sequences on-device
 * speech recognition and synthesis around ordinary chat turns.
 */
class VoiceLoopTest {

    private fun run(vararg events: VoiceLoopEvent): VoiceLoopState =
        events.fold(VoiceLoopState.Idle as VoiceLoopState, ::reduceVoiceLoop)

    @Test
    fun `start moves idle to listening`() {
        assertEquals(
            VoiceLoopState.Listening,
            run(VoiceLoopEvent.Start),
        )
    }

    @Test
    fun `speech result moves listening to thinking`() {
        assertEquals(
            VoiceLoopState.Thinking,
            run(VoiceLoopEvent.Start, VoiceLoopEvent.SpeechResult("hello")),
        )
    }

    @Test
    fun `blank speech result stays listening`() {
        assertEquals(
            VoiceLoopState.Listening,
            run(VoiceLoopEvent.Start, VoiceLoopEvent.SpeechResult("  ")),
        )
    }

    @Test
    fun `completed response moves thinking to speaking`() {
        assertEquals(
            VoiceLoopState.Speaking,
            run(
                VoiceLoopEvent.Start,
                VoiceLoopEvent.SpeechResult("hello"),
                VoiceLoopEvent.ResponseComplete,
            ),
        )
    }

    @Test
    fun `tts done loops back to listening`() {
        assertEquals(
            VoiceLoopState.Listening,
            run(
                VoiceLoopEvent.Start,
                VoiceLoopEvent.SpeechResult("hello"),
                VoiceLoopEvent.ResponseComplete,
                VoiceLoopEvent.TtsDone,
            ),
        )
    }

    @Test
    fun `stop returns any active state to idle`() {
        listOf(
            VoiceLoopState.Listening,
            VoiceLoopState.Thinking,
            VoiceLoopState.Speaking,
            VoiceLoopState.Error("mic busy"),
        ).forEach { state ->
            assertEquals(
                VoiceLoopState.Idle,
                reduceVoiceLoop(state, VoiceLoopEvent.Stop),
            )
        }
    }

    @Test
    fun `speech error surfaces the recognizer message`() {
        val state = run(VoiceLoopEvent.Start, VoiceLoopEvent.SpeechError("no match"))
        assertTrue(state is VoiceLoopState.Error)
        assertEquals("no match", (state as VoiceLoopState.Error).message)
    }

    @Test
    fun `tts error surfaces the synthesis message`() {
        val state = run(
            VoiceLoopEvent.Start,
            VoiceLoopEvent.SpeechResult("hello"),
            VoiceLoopEvent.ResponseComplete,
            VoiceLoopEvent.TtsError("engine failed"),
        )
        assertTrue(state is VoiceLoopState.Error)
        assertEquals("engine failed", (state as VoiceLoopState.Error).message)
    }

    @Test
    fun `start retries from error`() {
        assertEquals(
            VoiceLoopState.Listening,
            run(
                VoiceLoopEvent.Start,
                VoiceLoopEvent.SpeechError("no match"),
                VoiceLoopEvent.Start,
            ),
        )
    }

    @Test
    fun `out-of-order events leave the state unchanged`() {
        // A TTS-done arriving while listening is stale noise; ignore it.
        assertEquals(
            VoiceLoopState.Listening,
            run(VoiceLoopEvent.Start, VoiceLoopEvent.TtsDone),
        )
        // A response completing while idle is stale noise; ignore it.
        assertEquals(
            VoiceLoopState.Idle,
            run(VoiceLoopEvent.ResponseComplete),
        )
    }
}
