package com.tlg3d.hermes.security

/**
 * OS-protected secret storage (Android: Keystore via EncryptedSharedPreferences).
 * Secrets live here, never in config files or registry JSON.
 *
 * The generic [saveSecret]/[getSecret]/[clearSecret] methods back the
 * connection and SSH-host registries (one alias per entry id). The
 * per-gateway `saveApiKey` helpers are kept for existing callers and
 * delegate to the generic methods.
 */
interface SecretStore {
    fun saveSecret(alias: String, value: String)
    fun getSecret(alias: String): String?
    fun clearSecret(alias: String)

    fun saveApiKey(alias: String, apiKey: String) = saveSecret(alias, apiKey)
    fun getApiKey(alias: String): String? = getSecret(alias)
    fun clearApiKey(alias: String) = clearSecret(alias)
}
