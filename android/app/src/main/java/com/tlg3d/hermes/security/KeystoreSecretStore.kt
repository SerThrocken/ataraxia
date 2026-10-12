package com.tlg3d.hermes.security

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

/**
 * [SecretStore] backed by the Android Keystore via EncryptedSharedPreferences
 * (AES256_GCM value encryption, AES256_SIV key encryption). Replaces the
 * plaintext persistence the Electron app currently uses (SCOPE.md §5).
 *
 * Backs the gateway connection registry (`gateway-api-key:<id>`) and the SSH
 * host registry (`ssh-private-key:<id>`, `ssh-key-passphrase:<id>`,
 * `ssh-password:<id>`): secrets are encrypted at rest and never appear in
 * the registry JSON.
 */
class KeystoreSecretStore(context: Context) : SecretStore {

    private val prefs: SharedPreferences by lazy {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
        EncryptedSharedPreferences.create(
            context,
            PREFS_NAME,
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }

    override fun saveSecret(alias: String, value: String) {
        prefs.edit().putString(alias, value).apply()
    }

    override fun getSecret(alias: String): String? = prefs.getString(alias, null)

    override fun clearSecret(alias: String) {
        prefs.edit().remove(alias).apply()
    }

    companion object {
        private const val PREFS_NAME = "hermes_secrets"
    }
}
