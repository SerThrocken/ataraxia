package com.tlg3d.hermes.data

import android.content.Context

/**
 * Non-secret gateway settings (host). The API key is a secret and lives in
 * [com.tlg3d.hermes.security.SecretStore], never here.
 */
class GatewayPrefs(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    var host: String
        get() = prefs.getString(KEY_HOST, DEFAULT_HOST) ?: DEFAULT_HOST
        set(value) {
            prefs.edit().putString(KEY_HOST, value.trim()).apply()
        }

    companion object {
        const val PREFS_NAME = "hermes_gateway"
        const val KEY_HOST = "host"
        const val API_KEY_ALIAS = "gateway_api_key"

        /** Tailscale IP of the Acer gateway host. */
        const val DEFAULT_HOST = "100.101.250.118"
    }
}
