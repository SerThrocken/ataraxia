package com.tlg3d.hermes.ssh

import kotlinx.serialization.Serializable

/** How an SSH host authenticates: private key or password. */
@Serializable
enum class SshAuthKind { PRIVATE_KEY, PASSWORD }

/**
 * One saved SSH host: display name, host, port, username, auth kind.
 * Secrets (private key, optional key passphrase, password) are never stored
 * here — they live in [com.tlg3d.hermes.security.SecretStore] (Keystore)
 * under the per-host aliases below. Mirrors the Windows
 * `Hermes.Client.Core.SshHost` record.
 */
@Serializable
data class SshHost(
    /** Stable id; the registry generates one when blank. */
    val id: String = "",
    val name: String,
    val host: String,
    val port: Int = DEFAULT_SSH_PORT,
    val username: String,
    val authKind: SshAuthKind = SshAuthKind.PRIVATE_KEY,
) {
    /** Secret-store alias for the OpenSSH-format private key. */
    val privateKeyAlias: String get() = "ssh-private-key:$id"

    /** Secret-store alias for the private key's passphrase (optional). */
    val keyPassphraseAlias: String get() = "ssh-key-passphrase:$id"

    /** Secret-store alias for password auth. */
    val passwordAlias: String get() = "ssh-password:$id"

    companion object {
        const val DEFAULT_SSH_PORT = 22
    }
}
