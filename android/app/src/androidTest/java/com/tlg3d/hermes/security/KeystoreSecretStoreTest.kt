package com.tlg3d.hermes.security

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * On-device only (src/androidTest): Keystore-backed secret round-trips.
 * Requires a device or emulator — it does not run in the JVM unit-test
 * task, because EncryptedSharedPreferences needs the Android Keystore.
 */
class KeystoreSecretStoreTest {

    private val store = KeystoreSecretStore(
        ApplicationProvider.getApplicationContext<Context>(),
    )

    @Test
    fun roundTrip_saveGetClear() {
        val alias = "test-${System.nanoTime()}"
        try {
            assertNull(store.getSecret(alias))
            store.saveSecret(alias, "super-secret-value")
            assertEquals("super-secret-value", store.getSecret(alias))
            store.clearSecret(alias)
            assertNull(store.getSecret(alias))
        } finally {
            store.clearSecret(alias)
        }
    }

    @Test
    fun apiKeyHelpers_delegateToGenericMethods() {
        val alias = "test-key-${System.nanoTime()}"
        try {
            store.saveApiKey(alias, "api-key-value")
            assertEquals("api-key-value", store.getApiKey(alias))
            store.clearApiKey(alias)
            assertNull(store.getApiKey(alias))
        } finally {
            store.clearApiKey(alias)
        }
    }

    @Test
    fun getSecret_returnsNullWhenMissing() {
        assertNull(store.getSecret("missing-${System.nanoTime()}"))
    }
}
