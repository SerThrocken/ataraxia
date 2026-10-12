package com.tlg3d.hermes.data

import com.tlg3d.hermes.testutil.InMemoryKeyValueStore
import com.tlg3d.hermes.testutil.InMemorySecretStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ConnectionRegistryTest {
    private val store = InMemoryKeyValueStore()
    private val secrets = InMemorySecretStore()
    private val registry = GatewayConnectionRegistry(store, secrets)

    private fun conn(name: String = "Acer", host: String = "100.101.250.118") =
        GatewayConnection(name = name, host = host)

    @Test
    fun addAssignsIdAndLists() {
        val id = registry.add(conn())

        assertTrue(id.isNotBlank())
        val stored = registry.list().single()
        assertEquals(id, stored.id)
        assertEquals("Acer", stored.name)
        assertEquals("100.101.250.118", stored.host)
        assertEquals(8642, stored.port)
        assertEquals("http://100.101.250.118:8642", stored.baseUrl)
    }

    @Test
    fun firstConnectionBecomesActive() {
        val id = registry.add(conn())

        assertEquals(id, registry.getActive()?.id)
    }

    @Test
    fun secondConnectionDoesNotStealActive() {
        val first = registry.add(conn("Acer"))
        registry.add(conn("Cloud", "example.com"))

        assertEquals(first, registry.getActive()?.id)
    }

    @Test
    fun setActiveIdSwitchesActive() {
        registry.add(conn("Acer"))
        val second = registry.add(conn("Cloud", "example.com"))

        registry.setActiveId(second)

        assertEquals(second, registry.getActive()?.id)
    }

    @Test(expected = IllegalArgumentException::class)
    fun duplicateIdRejected() {
        registry.add(conn().copy(id = "dup"))
        registry.add(conn("Other").copy(id = "dup"))
    }

    @Test
    fun invalidRejected() {
        // blank name
        assertThrowsIAE { registry.add(GatewayConnection(name = "", host = "example.com")) }
        // bad host
        assertThrowsIAE { registry.add(GatewayConnection(name = "A", host = "not a host!")) }
        assertThrowsIAE { registry.add(GatewayConnection(name = "A", host = "")) }
        // bad port
        assertThrowsIAE { registry.add(GatewayConnection(name = "A", host = "example.com", port = 0)) }
        assertThrowsIAE { registry.add(GatewayConnection(name = "A", host = "example.com", port = 70000)) }
        // unknown active id
        assertThrowsIAE { registry.setActiveId("nope") }
    }

    @Test
    fun updateReplaces() {
        val id = registry.add(conn())

        registry.update(GatewayConnection(
            id = id, name = "Acer 2", host = "example.com", port = 9999, useTls = true))

        val stored = registry.get(id)
        assertNotNull(stored)
        assertEquals("Acer 2", stored!!.name)
        assertEquals("example.com", stored.host)
        assertEquals(9999, stored.port)
        assertTrue(stored.useTls)
        assertEquals("https://example.com:9999", stored.baseUrl)
    }

    @Test
    fun deleteRemovesConnectionAndSecret() {
        val id = registry.add(conn())
        registry.setApiKey(id, "secret-key-1234567890")

        registry.delete(id)

        assertTrue(registry.list().isEmpty())
        assertFalse(secrets.hasSecret("gateway-api-key:$id"))
    }

    @Test
    fun deleteFallsBackActiveToFirstRemaining() {
        val first = registry.add(conn("Acer"))
        val second = registry.add(conn("Cloud", "example.com"))

        registry.delete(first)

        assertEquals(second, registry.getActive()?.id)
    }

    @Test
    fun apiKeyRoundTripsViaAlias() {
        val id = registry.add(conn())

        registry.setApiKey(id, "secret-key-1234567890")

        assertEquals("secret-key-1234567890", registry.getApiKey(id))
        assertTrue(secrets.hasSecret("gateway-api-key:$id"))
    }

    @Test
    fun persistedJsonContainsNoSecrets() {
        val id = registry.add(conn())
        registry.setApiKey(id, "super-secret-api-key-1234567890")

        val raw = store.raw[GatewayConnectionRegistry.STORE_KEY]
        assertNotNull(raw)
        assertFalse(raw!!.contains("super-secret-api-key-1234567890"))
    }

    @Test
    fun migrateFromLegacyImportsHostAndMovesKey() {
        secrets.saveApiKey(GatewayPrefs.API_KEY_ALIAS, "legacy-key-1234567890")

        val migrated = registry.migrateFromLegacy(
            "100.101.250.118", GatewayPrefs.API_KEY_ALIAS, hostWasExplicitlySet = true)

        assertTrue(migrated)
        val entry = registry.list().single()
        assertEquals("imported", entry.id)
        assertEquals("100.101.250.118", entry.host)
        assertEquals(8642, entry.port)
        assertEquals(entry.id, registry.getActive()?.id)
        // The key moved: present under the new alias, gone from the old one.
        assertEquals("legacy-key-1234567890", registry.getApiKey(entry.id))
        assertFalse(secrets.hasSecret(GatewayPrefs.API_KEY_ALIAS))
    }

    @Test
    fun migrateFromLegacyIsIdempotent() {
        secrets.saveApiKey(GatewayPrefs.API_KEY_ALIAS, "legacy-key-1234567890")

        assertTrue(registry.migrateFromLegacy(
            "100.101.250.118", GatewayPrefs.API_KEY_ALIAS, hostWasExplicitlySet = true))
        assertFalse(registry.migrateFromLegacy(
            "100.101.250.118", GatewayPrefs.API_KEY_ALIAS, hostWasExplicitlySet = true))

        assertEquals(1, registry.list().size)
    }

    @Test
    fun migrateFromLegacySkipsWhenNothingConfigured() {
        val migrated = registry.migrateFromLegacy(
            "100.101.250.118", GatewayPrefs.API_KEY_ALIAS, hostWasExplicitlySet = false)

        assertFalse(migrated)
        assertTrue(registry.list().isEmpty())
        assertNull(registry.getActive())
    }

    @Test
    fun migrateFromLegacyImportsWhenKeyExistsEvenIfHostDefault() {
        secrets.saveApiKey(GatewayPrefs.API_KEY_ALIAS, "legacy-key-1234567890")

        val migrated = registry.migrateFromLegacy(
            "100.101.250.118", GatewayPrefs.API_KEY_ALIAS, hostWasExplicitlySet = false)

        assertTrue(migrated)
        assertEquals(1, registry.list().size)
    }

    @Test
    fun migrateFromLegacySkipsGarbageHost() {
        secrets.saveApiKey(GatewayPrefs.API_KEY_ALIAS, "legacy-key-1234567890")

        val migrated = registry.migrateFromLegacy(
            "not a host!", GatewayPrefs.API_KEY_ALIAS, hostWasExplicitlySet = true)

        assertFalse(migrated)
        assertTrue(registry.list().isEmpty())
        // Second run stays skipped (idempotent): no phantom entry, no crash loop.
        assertFalse(
            registry.migrateFromLegacy(
                "not a host!", GatewayPrefs.API_KEY_ALIAS, hostWasExplicitlySet = true))
    }

    private fun assertThrowsIAE(block: () -> Unit) {
        try {
            block()
        } catch (e: IllegalArgumentException) {
            return
        }
        throw AssertionError("Expected IllegalArgumentException")
    }
}
