package com.tlg3d.hermes.ssh

/**
 * Shared terminal-session contract between session *backends* and terminal
 * *views* — the Kotlin mirror of the Windows `Hermes.Client.Core.ITerminalSession`.
 * See TERMINAL-SEAM.md at the repo root for the full contract both sides follow.
 *
 * Contract summary:
 * - One instance = one live session (one SSH shell channel, or one local PTY).
 *   It is not reused after [close].
 * - [Listener.onData] carries raw terminal bytes from the remote end. The view
 *   feeds them verbatim into its terminal emulator; the backend never
 *   interprets escape sequences and never renders.
 * - [write] carries keystrokes / pasted input toward the remote end, verbatim.
 * - [resize] notifies the remote PTY of the view's new size in character cells.
 * - [Listener.onDisconnected] fires exactly once when the session ends for any
 *   reason (remote close, network drop, [close]). After it fires the instance
 *   is done — the view drops its reference.
 * - Backends own all transport threads and pumps; the view only calls the
 *   suspend functions and sets the listener. Callbacks may arrive on
 *   background threads — views marshal to the main thread.
 */
interface TerminalSession {
    /** Display title (host name for SSH, shell name for local PTY). */
    val title: String

    /** Whether the transport is currently up. */
    val isConnected: Boolean

    interface Listener {
        /** Raw terminal output from the remote end. See contract above. */
        fun onData(data: ByteArray)

        /** Fires exactly once when the session ends. */
        fun onDisconnected()
    }

    fun setListener(listener: Listener?)

    /**
     * Establishes the session. May throw [SshUnknownHostKeyException] (SSH
     * backends) when the server's host key is not pinned — the UI shows the
     * fingerprint, and on approval pins it via [SshHostKeyTrust] and retries.
     */
    suspend fun connect()

    /** Sends raw input bytes (keystrokes, paste) to the remote end. */
    suspend fun write(data: ByteArray)

    /** UTF-8 convenience overload for [write]. */
    suspend fun write(text: String) = write(text.toByteArray(Charsets.UTF_8))

    /** Notifies the remote PTY of the view's size in character cells. */
    suspend fun resize(columns: Int, rows: Int)

    /**
     * Closes the session: stops the pump, closes the transport, fires
     * [Listener.onDisconnected]. Idempotent.
     */
    suspend fun close()
}
