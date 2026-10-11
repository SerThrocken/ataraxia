package com.tlg3d.hermes.network

/**
 * Connection details for one Hermes gateway.
 *
 * The port comes from SCOPE.md: the CLI gateway listens on 8642 with
 * `Authorization: Bearer <API_SERVER_KEY>` auth. The host is always supplied
 * by the user (Settings); nothing here hardcodes a machine.
 */
data class GatewayConfig(
    val baseUrl: String,
    val apiKey: String = "",
) {
    init {
        require(baseUrl.isNotBlank()) { "baseUrl must not be blank" }
    }

    companion object {
        /** CLI gateway port, per SCOPE.md §2. */
        const val DEFAULT_PORT = 8642

        /** Builds a config for a LAN/Tailscale host; the key comes from [com.tlg3d.hermes.security.SecretStore]. */
        fun forHost(host: String, apiKey: String = "") =
            GatewayConfig(baseUrl = "http://$host:$DEFAULT_PORT", apiKey = apiKey)
    }
}
