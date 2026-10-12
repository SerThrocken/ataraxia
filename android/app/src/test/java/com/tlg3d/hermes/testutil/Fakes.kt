package com.tlg3d.hermes.testutil

import com.tlg3d.hermes.data.KeyValueStore
import com.tlg3d.hermes.security.SecretStore

/**
 * Test-only (src/test, never packaged) in-memory implementations of the
 * storage interfaces, so registry logic is unit-testable on the JVM.
 */
class InMemoryKeyValueStore : KeyValueStore {
    private val map = mutableMapOf<String, String>()
    val raw: Map<String, String> get() = map

    override fun getString(key: String): String? = map[key]
    override fun putString(key: String, value: String) {
        map[key] = value
    }
    override fun remove(key: String) {
        map.remove(key)
    }
}

class InMemorySecretStore : SecretStore {
    private val map = mutableMapOf<String, String>()

    override fun saveSecret(alias: String, value: String) {
        map[alias] = value
    }
    override fun getSecret(alias: String): String? = map[alias]
    override fun clearSecret(alias: String) {
        map.remove(alias)
    }

    fun hasSecret(alias: String): Boolean = map.containsKey(alias)
}
