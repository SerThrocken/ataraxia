package com.tlg3d.hermes.data

import com.tlg3d.hermes.security.SecretStore
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.util.UUID

/**
 * One saved Hermes gateway: display name, host, port, TLS flag. The Bearer
 * API key is never stored here — it lives in [SecretStore] under
 * [secretAlias] (one alias per connection id). Mirrors the Windows
 * `Hermes.Client.Core.GatewayConnection` record.
 */
@Serializable
data class GatewayConnection(
    /** Stable id; the registry generates one when blank. */
    val id: String = "",
    val name: String,
    val host: String,
    val port: Int = DEFAULT_GATEWAY_PORT,
    val useTls: Boolean = false,
) {
    /** Secret-store alias holding this connection's Bearer API key. */
    val secretAlias: String get() = "gateway-api-key:$id"

    /** Full base URL, e.g. http://100.101.250.118:8642. */
    val baseUrl: String get() = "${if (useTls) "https" else "http"}://${host.trim()}:$port"

    companion object {
        /** CLI gateway port, per SCOPE.md §2. */
        const val DEFAULT_GATEWAY_PORT = 8642
    }
}

@Serializable
private data class GatewayRegistryState(
    val activeId: String? = null,
    val connections: List<GatewayConnection> = emptyList(),
    val migratedFromSingleConnection: Boolean = false,
)

/**
 * Registry of saved gateway connections, persisted as JSON in [KeyValueStore]
 * (no secrets in the JSON — those live in [SecretStore] under per-connection
 * aliases). The first connection added becomes the active one.
 */
class GatewayConnectionRegistry(
    private val store: KeyValueStore,
    private val secrets: SecretStore,
) {
    fun list(): List<GatewayConnection> = load().connections

    fun get(id: String): GatewayConnection? = load().connections.firstOrNull { it.id == id }

    fun getActive(): GatewayConnection? {
        val state = load()
        return state.activeId?.let { activeId -> state.connections.firstOrNull { it.id == activeId } }
    }

    fun setActiveId(id: String) {
        val state = load()
        require(state.connections.any { it.id == id }) { "No gateway connection with id '$id'." }
        save(state.copy(activeId = id))
    }

    /** @return the stored connection's id (generated when blank). */
    fun add(connection: GatewayConnection): String {
        validate(connection)
        val state = load()
        val id = connection.id.ifBlank { newId() }.trim()
        require(state.connections.none { it.id == id }) {
            "A gateway connection with id '$id' already exists."
        }
        val stored = connection.copy(id = id, name = connection.name.trim(), host = connection.host.trim())
        save(state.copy(
            connections = state.connections + stored,
            activeId = state.activeId ?: id,
        ))
        return id
    }

    fun update(connection: GatewayConnection) {
        require(connection.id.isNotBlank()) { "Connection id is required for update." }
        validate(connection)
        val state = load()
        val index = state.connections.indexOfFirst { it.id == connection.id }
        require(index >= 0) { "No gateway connection with id '${connection.id}'." }
        val stored = connection.copy(name = connection.name.trim(), host = connection.host.trim())
        save(state.copy(connections = state.connections.toMutableList().also { it[index] = stored }))
    }

    /** Removes the connection and deletes its API key from the secret store. */
    fun delete(id: String) {
        val state = load()
        require(state.connections.any { it.id == id }) { "No gateway connection with id '$id'." }
        secrets.clearSecret("gateway-api-key:$id")
        val remaining = state.connections.filterNot { it.id == id }
        save(state.copy(
            connections = remaining,
            activeId = if (state.activeId == id) remaining.firstOrNull()?.id else state.activeId,
        ))
    }

    fun getApiKey(id: String): String? = secrets.getSecret("gateway-api-key:$id")

    fun setApiKey(id: String, apiKey: String) {
        require(id.isNotBlank()) { "Connection id is required." }
        require(apiKey.isNotBlank()) { "API key must not be blank." }
        secrets.saveSecret("gateway-api-key:${id.trim()}", apiKey.trim())
    }

    fun deleteApiKey(id: String) = secrets.clearSecret("gateway-api-key:$id")

    /**
     * One-time migration: the pre-registry single connection (host pref +
     * legacy API-key alias) becomes the first registry entry. The key is
     * *moved*, not copied — afterwards it lives only under the new
     * per-connection alias. Idempotent: returns false when there is nothing
     * to import or the migration already ran.
     *
     * @param hostWasExplicitlySet true when the user ever saved a host
     * (vs. the app default). When false and no key exists, there is nothing
     * to import and no phantom entry is created.
     */
    fun migrateFromLegacy(host: String, legacyApiKeyAlias: String, hostWasExplicitlySet: Boolean): Boolean {
        val state = load()
        if (state.migratedFromSingleConnection) return false

        var migrated = false
        val key = secrets.getApiKey(legacyApiKeyAlias)
        if (hostWasExplicitlySet || !key.isNullOrBlank()) {
            // The legacy host pref is untrusted input: validate before it
            // becomes a registry entry, so a garbage value can't poison the
            // registry lazy and crash every gatewayClient() call.
            if (isValidHost(host)) {
                val id = if (state.connections.any { it.id == IMPORTED_ID }) newId() else IMPORTED_ID
                add(GatewayConnection(
                    id = id,
                    name = "Imported (${host.trim()})",
                    host = host.trim(),
                ))
                if (!key.isNullOrBlank()) {
                    secrets.saveSecret("gateway-api-key:$id", key)
                    secrets.clearApiKey(legacyApiKeyAlias)
                }
                migrated = true
            }
        }

        save(load().copy(migratedFromSingleConnection = true))
        return migrated
    }

    private fun validate(connection: GatewayConnection) {
        require(connection.name.isNotBlank()) { "Connection name is required." }
        require(isValidHost(connection.host)) {
            "'${connection.host}' is not a valid host name or IP address."
        }
        require(connection.port in 1..65535) { "Port must be between 1 and 65535." }
    }

    private fun load(): GatewayRegistryState =
        store.getString(STORE_KEY)?.let { Json.decodeFromString<GatewayRegistryState>(it) }
            ?: GatewayRegistryState()

    private fun save(state: GatewayRegistryState) =
        store.putString(STORE_KEY, Json.encodeToString(state))

    companion object {
        const val STORE_KEY = "gateway_connections"

        private const val IMPORTED_ID = "imported"

        private fun newId(): String = UUID.randomUUID().toString().replace("-", "").take(12)

        internal fun isValidHost(host: String): Boolean {
            val h = host.trim()
            if (h.isEmpty() || h.any { it.isWhitespace() } || h.contains("://")) return false
            // IPv4 / IPv6 literal or dotted hostname: labels of letters, digits, hyphens, dots.
            return h.matches(Regex("[A-Za-z0-9.:\\[\\]-]+"))
        }
    }
}
