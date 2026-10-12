package com.tlg3d.hermes.ssh

import com.tlg3d.hermes.data.KeyValueStore
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/**
 * Pin store for SSH host keys. Fingerprints are `SHA256:base64` (OpenSSH
 * format, no padding — identical to the Windows side) — never the key
 * itself, never a secret. The session factory consults this before trusting
 * a presented host key; the UI pins a fingerprint here after the user
 * approves it.
 */
interface SshHostKeyTrust {
    fun getPinnedFingerprint(host: String, port: Int): String?
    fun pinFingerprint(host: String, port: Int, fingerprint: String)
}

/**
 * [SshHostKeyTrust] backed by [KeyValueStore]: one JSON object,
 * `{"host:port": "SHA256:..."}`, under [STORE_KEY]. The JSON holds
 * fingerprints only — never keys, never secrets.
 */
class PrefsSshHostKeyTrust(private val store: KeyValueStore) : SshHostKeyTrust {
    override fun getPinnedFingerprint(host: String, port: Int): String? =
        load()[keyFor(host, port)]

    override fun pinFingerprint(host: String, port: Int, fingerprint: String) {
        require(host.isNotBlank()) { "Host is required." }
        require(fingerprint.isNotBlank()) { "Fingerprint is required." }
        val pins = load().toMutableMap()
        pins[keyFor(host, port)] = fingerprint.trim()
        store.putString(STORE_KEY, Json.encodeToString(pins))
    }

    private fun load(): Map<String, String> =
        store.getString(STORE_KEY)
            ?.let { Json.decodeFromString<Map<String, String>>(it) }
            ?: emptyMap()

    companion object {
        const val STORE_KEY = "ssh_known_hosts"

        private fun keyFor(host: String, port: Int) =
            "${host.trim().lowercase()}:$port"
    }
}
