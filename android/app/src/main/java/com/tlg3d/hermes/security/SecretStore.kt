package com.tlg3d.hermes.security

/**
 * Per-gateway credential storage. Secrets live here, never in config files.
 */
interface SecretStore {
    fun saveApiKey(alias: String, apiKey: String)
    fun getApiKey(alias: String): String?
    fun clearApiKey(alias: String)
}
