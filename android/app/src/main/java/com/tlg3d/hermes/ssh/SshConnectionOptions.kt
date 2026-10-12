package com.tlg3d.hermes.ssh

/**
 * Validated, secret-free SSH dial parameters built from a saved [SshHost].
 * Pure validation and string handling — no sockets, no keys — so it is
 * unit-testable on the JVM. Mirrors the Windows
 * `Hermes.Client.Core.SshConnectionOptions` record.
 */
data class SshConnectionOptions(
    val host: String,
    val port: Int = SshHost.DEFAULT_SSH_PORT,
    val username: String,
    val authKind: SshAuthKind = SshAuthKind.PRIVATE_KEY,
) {
    /** @return null when valid; otherwise a human-readable reason. */
    fun validate(): String? {
        if (!isValidHost(host)) {
            return "Host '$host' is not a valid host name or IP address."
        }
        if (port !in 1..65535) {
            return "Port must be between 1 and 65535."
        }
        if (username.isBlank()) {
            return "Username is required."
        }
        return null
    }

    /**
     * Log-safe description (`user@host:port`). Never includes secrets —
     * there are none on this object by construction.
     */
    fun describe(): String = "${username.trim()}@${host.trim()}:$port"

    companion object {
        fun fromHost(host: SshHost) = SshConnectionOptions(
            host = host.host,
            port = host.port,
            username = host.username,
            authKind = host.authKind,
        )
    }
}

/**
 * Host-name sanity check (JVM-safe, no Android APIs): non-blank, no
 * whitespace, no scheme, hostname/IPv4/IPv6-literal characters only.
 */
internal fun isValidHost(host: String): Boolean {
    val h = host.trim()
    if (h.isEmpty() || h.any { it.isWhitespace() } || h.contains("://")) return false
    return h.matches(Regex("[A-Za-z0-9.:\\[\\]-]+"))
}
