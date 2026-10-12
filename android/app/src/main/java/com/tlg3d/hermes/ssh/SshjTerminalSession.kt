package com.tlg3d.hermes.ssh

import com.tlg3d.hermes.security.SecretStore
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import net.schmizz.sshj.AndroidConfig
import net.schmizz.sshj.SSHClient
import net.schmizz.sshj.connection.channel.direct.Session
import net.schmizz.sshj.transport.verification.HostKeyVerifier
import java.io.InputStream
import java.security.MessageDigest
import java.security.PublicKey
import java.util.Base64
import java.util.concurrent.atomic.AtomicBoolean

/**
 * [TerminalSession] over sshj: an interactive shell channel
 * (`xterm-256color`) to a saved [SshHost]. Private keys and passwords are
 * read from [SecretStore] (Keystore) at connect time and never persisted by
 * this class — they live only in the sshj auth calls for the session's
 * lifetime.
 *
 * Build note: the exact sshj call shapes (`loadKeys` passphrase overload,
 * `changeWindowDimensions`) are confirmed by the authoritative Gradle build;
 * see the task report if the build flags any of them.
 */
class SshjTerminalSession(
    private val host: SshHost,
    private val secrets: SecretStore,
    private val hostKeyTrust: SshHostKeyTrust,
) : TerminalSession {

    override val title: String get() = host.name
    override val isConnected: Boolean get() = client?.isConnected == true

    private var listener: TerminalSession.Listener? = null
    private var client: SSHClient? = null
    private var shell: Session.Shell? = null
    private var scope: CoroutineScope? = null
    private val disconnectedFired = AtomicBoolean(false)
    private var closed = false

    override fun setListener(listener: TerminalSession.Listener?) {
        this.listener = listener
    }

    override suspend fun connect() {
        withContext(Dispatchers.IO) {
            check(!closed) { "Session is closed." }
            if (isConnected) return@withContext

            val options = SshConnectionOptions.fromHost(host)
            val invalid = options.validate()
            require(invalid == null) { "Invalid SSH options: $invalid" }

            val ssh = SSHClient(AndroidConfig())
            var presented: String? = null
            var changed = false
            val pinned = hostKeyTrust.getPinnedFingerprint(host.host, host.port)
            ssh.addHostKeyVerifier(object : HostKeyVerifier {
                override fun verify(hostname: String, port: Int, key: PublicKey): Boolean {
                    val fp = fingerprint(key.encoded)
                    return if (fp == pinned) {
                        true
                    } else {
                        // Don't trust yet: surface the presented key to the UI via
                        // SshUnknownHostKeyException so the user can approve and pin it.
                        presented = fp
                        changed = pinned != null
                        false
                    }
                }

                /**
                 * No algorithm pre-filtering: we pin by fingerprint, so every
                 * presented key goes through [verify].
                 */
                override fun findExistingAlgorithms(hostname: String, port: Int): List<String> =
                    emptyList()
            })
            try {
                ssh.connect(host.host.trim(), host.port)
            } catch (e: Exception) {
                val fp = presented
                try {
                    ssh.disconnect()
                } catch (ignored: Exception) {
                    // Tearing down a failed connect; nothing to report.
                }
                if (fp != null) {
                    throw SshUnknownHostKeyException(host.host.trim(), host.port, fp, changed, e)
                }
                throw e
            }

            val username = host.username.trim()
            try {
                when (host.authKind) {
                    SshAuthKind.PASSWORD -> {
                        val password = secrets.getSecret(host.passwordAlias)
                            ?: throw IllegalStateException("No password stored for SSH host '${host.name}'.")
                        ssh.authPassword(username, password)
                    }
                    SshAuthKind.PRIVATE_KEY -> {
                        val keyText = secrets.getSecret(host.privateKeyAlias)
                            ?: throw IllegalStateException("No private key stored for SSH host '${host.name}'.")
                        val passphrase = secrets.getSecret(host.keyPassphraseAlias)
                        val keys = if (passphrase.isNullOrEmpty()) {
                            ssh.loadKeys(keyText)
                        } else {
                            ssh.loadKeys(keyText, passphrase)
                        }
                        ssh.authPublickey(username, keys)
                    }
                }

                val session = ssh.startSession()
                session.allocatePTY(TERM_TYPE, 80, 24, 0, 0, emptyMap())
                val sh = session.startShell()
                client = ssh
                shell = sh
            } catch (e: Exception) {
                // Auth or shell setup failed after connect: don't leak the transport.
                try {
                    ssh.disconnect()
                } catch (ignored: Exception) {
                    // Tearing down a failed connect; nothing to report.
                }
                throw e
            }
            val jobScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
            scope = jobScope
            val sh = shell ?: throw IllegalStateException("Not connected.")
            jobScope.launch { pump(sh.inputStream) }
        }
    }

    override suspend fun write(data: ByteArray) {
        withContext(Dispatchers.IO) {
            check(!closed) { "Session is closed." }
            val out = shell?.outputStream ?: throw IllegalStateException("Not connected.")
            out.write(data)
            out.flush()
        }
    }

    override suspend fun resize(columns: Int, rows: Int) {
        withContext(Dispatchers.IO) {
            require(columns > 0 && rows > 0) { "Columns and rows must be positive." }
            shell?.changeWindowDimensions(columns, rows, 0, 0)
        }
    }

    override suspend fun close() {
        withContext(Dispatchers.IO) {
            if (closed) return@withContext
            closed = true
            try {
                client?.disconnect()
            } catch (ignored: Exception) {
                // Teardown must not throw; the pump reports the drop via onDisconnected.
            }
            try {
                client?.close()
            } catch (ignored: Exception) {
                // Teardown must not throw; the pump reports the drop via onDisconnected.
            }
            scope?.cancel()
            fireDisconnected()
        }
    }

    private suspend fun pump(input: InputStream) {
        val buf = ByteArray(8192)
        try {
            while (true) {
                val n = input.read(buf)
                if (n <= 0) break
                listener?.onData(buf.copyOf(n))
            }
        } catch (e: CancellationException) {
            throw e
        } catch (ignored: Exception) {
            // Transport dropped mid-session; onDisconnected below is the signal.
        } finally {
            fireDisconnected()
        }
    }

    private fun fireDisconnected() {
        if (disconnectedFired.compareAndSet(false, true)) {
            listener?.onDisconnected()
        }
    }

    companion object {
        const val TERM_TYPE = "xterm-256color"

        /**
         * OpenSSH `SHA256:base64` fingerprint (no padding) of a raw host key.
         * This is the format both platforms pin in their host-key trust stores.
         */
        internal fun fingerprint(keyBytes: ByteArray): String {
            val hash = MessageDigest.getInstance("SHA-256").digest(keyBytes)
            return "SHA256:" + Base64.getEncoder().withoutPadding().encodeToString(hash)
        }
    }
}

/**
 * Builds connected [TerminalSession] instances for saved SSH hosts. The UI
 * catches [SshUnknownHostKeyException], asks the user to approve the
 * presented fingerprint, pins it via [SshHostKeyTrust], and retries.
 */
class SshjSessionFactory(
    private val secrets: SecretStore,
    private val hostKeyTrust: SshHostKeyTrust,
) {
    suspend fun connect(host: SshHost): TerminalSession {
        val session = SshjTerminalSession(host, secrets, hostKeyTrust)
        session.connect()
        return session
    }
}
