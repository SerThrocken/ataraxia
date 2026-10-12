package com.tlg3d.hermes.ssh

import com.tlg3d.hermes.data.KeyValueStore
import com.tlg3d.hermes.security.SecretStore
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.util.UUID

@Serializable
private data class SshRegistryState(
    val hosts: List<SshHost> = emptyList(),
)

/**
 * Registry of saved SSH hosts, persisted as JSON in [KeyValueStore]
 * (no secrets in the JSON). Mirrors the Windows
 * `Hermes.Client.Core.SshHostRegistry`.
 */
class SshHostRegistry(
    private val store: KeyValueStore,
    private val secrets: SecretStore,
) {
    fun list(): List<SshHost> = load().hosts

    fun get(id: String): SshHost? = load().hosts.firstOrNull { it.id == id }

    /** @return the stored host's id (generated when blank). */
    fun add(host: SshHost): String {
        val invalid = SshConnectionOptions.fromHost(host).validate()
        require(invalid == null) { "Invalid SSH options: $invalid" }
        require(host.name.isNotBlank()) { "Host name is required." }
        val state = load()
        val id = host.id.ifBlank { newId() }.trim()
        require(state.hosts.none { it.id == id }) { "An SSH host with id '$id' already exists." }
        val stored = host.copy(
            id = id,
            name = host.name.trim(),
            host = host.host.trim(),
            username = host.username.trim(),
        )
        save(state.copy(hosts = state.hosts + stored))
        return id
    }

    fun update(host: SshHost) {
        require(host.id.isNotBlank()) { "Host id is required for update." }
        val invalid = SshConnectionOptions.fromHost(host).validate()
        require(invalid == null) { "Invalid SSH options: $invalid" }
        require(host.name.isNotBlank()) { "Host name is required." }
        val state = load()
        val index = state.hosts.indexOfFirst { it.id == host.id }
        require(index >= 0) { "No SSH host with id '${host.id}'." }
        val stored = host.copy(
            name = host.name.trim(),
            host = host.host.trim(),
            username = host.username.trim(),
        )
        save(state.copy(hosts = state.hosts.toMutableList().also { it[index] = stored }))
    }

    /** Removes the host and deletes all of its secrets from the secret store. */
    fun delete(id: String) {
        val state = load()
        require(state.hosts.any { it.id == id }) { "No SSH host with id '$id'." }
        secrets.clearSecret("ssh-private-key:$id")
        secrets.clearSecret("ssh-key-passphrase:$id")
        secrets.clearSecret("ssh-password:$id")
        save(state.copy(hosts = state.hosts.filterNot { it.id == id }))
    }

    fun getPrivateKey(id: String): String? = secrets.getSecret("ssh-private-key:$id")

    fun setPrivateKey(id: String, privateKey: String) {
        require(id.isNotBlank()) { "Host id is required." }
        require(privateKey.isNotBlank()) { "Private key must not be blank." }
        secrets.saveSecret("ssh-private-key:${id.trim()}", privateKey)
    }

    fun getKeyPassphrase(id: String): String? = secrets.getSecret("ssh-key-passphrase:$id")

    fun setKeyPassphrase(id: String, passphrase: String) {
        require(id.isNotBlank()) { "Host id is required." }
        require(passphrase.isNotBlank()) { "Passphrase must not be blank." }
        secrets.saveSecret("ssh-key-passphrase:${id.trim()}", passphrase)
    }

    fun getPassword(id: String): String? = secrets.getSecret("ssh-password:$id")

    fun setPassword(id: String, password: String) {
        require(id.isNotBlank()) { "Host id is required." }
        require(password.isNotBlank()) { "Password must not be blank." }
        secrets.saveSecret("ssh-password:${id.trim()}", password)
    }

    private fun load(): SshRegistryState =
        store.getString(STORE_KEY)?.let { Json.decodeFromString<SshRegistryState>(it) }
            ?: SshRegistryState()

    private fun save(state: SshRegistryState) =
        store.putString(STORE_KEY, Json.encodeToString(state))

    companion object {
        const val STORE_KEY = "ssh_hosts"

        private fun newId(): String = UUID.randomUUID().toString().replace("-", "").take(12)
    }
}
