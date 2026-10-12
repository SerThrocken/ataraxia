package com.tlg3d.hermes.ssh

import com.tlg3d.hermes.testutil.InMemoryKeyValueStore
import com.tlg3d.hermes.testutil.InMemorySecretStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SshHostRegistryTest {
    private val store = InMemoryKeyValueStore()
    private val secrets = InMemorySecretStore()
    private val registry = SshHostRegistry(store, secrets)

    private fun host(name: String = "Acer", hostname: String = "100.101.250.118") =
        SshHost(name = name, host = hostname, username = "tlcro")

    @Test
    fun addAssignsIdAndLists() {
        val id = registry.add(host())

        assertTrue(id.isNotBlank())
        val stored = registry.list().single()
        assertEquals(id, stored.id)
        assertEquals("Acer", stored.name)
        assertEquals("100.101.250.118", stored.host)
        assertEquals(22, stored.port)
        assertEquals("tlcro", stored.username)
        assertEquals(SshAuthKind.PRIVATE_KEY, stored.authKind)
    }

    @Test(expected = IllegalArgumentException::class)
    fun duplicateIdRejected() {
        registry.add(host().copy(id = "dup"))
        registry.add(host("Other").copy(id = "dup"))
    }

    @Test
    fun invalidRejected() {
        // blank name
        assertThrowsIAE { registry.add(SshHost(name = "", host = "example.com", username = "u")) }
        // bad host
        assertThrowsIAE { registry.add(SshHost(name = "A", host = "not a host!", username = "u")) }
        // bad port
        assertThrowsIAE { registry.add(SshHost(name = "A", host = "example.com", port = 0, username = "u")) }
        assertThrowsIAE {
            registry.add(SshHost(name = "A", host = "example.com", port = 70000, username = "u"))
        }
        // blank username
        assertThrowsIAE { registry.add(SshHost(name = "A", host = "example.com", username = "  ")) }
        // unknown id
        assertNull(registry.get("nope"))
    }

    @Test
    fun updateReplaces() {
        val id = registry.add(host())

        registry.update(SshHost(
            id = id, name = "Acer 2", host = "example.com", port = 2222,
            username = "admin", authKind = SshAuthKind.PASSWORD))

        val stored = registry.get(id)
        assertNotNull(stored)
        assertEquals("Acer 2", stored!!.name)
        assertEquals(2222, stored.port)
        assertEquals("admin", stored.username)
        assertEquals(SshAuthKind.PASSWORD, stored.authKind)
    }

    @Test
    fun privateKeyRoundTripsViaAlias() {
        val id = registry.add(host())
        val key = "-----BEGIN OPENSSH PRIVATE KEY-----\nfake\n-----END OPENSSH PRIVATE KEY-----"

        registry.setPrivateKey(id, key)

        assertEquals(key, registry.getPrivateKey(id))
        assertTrue(secrets.hasSecret("ssh-private-key:$id"))
    }

    @Test
    fun passwordAndPassphraseRoundTripViaAliases() {
        val id = registry.add(host().copy(authKind = SshAuthKind.PASSWORD))

        registry.setPassword(id, "hunter2-hunter2-hunter2")
        registry.setKeyPassphrase(id, "correct horse battery staple")

        assertEquals("hunter2-hunter2-hunter2", registry.getPassword(id))
        assertEquals("correct horse battery staple", registry.getKeyPassphrase(id))
    }

    @Test
    fun deleteRemovesHostAndAllSecrets() {
        val id = registry.add(host())
        registry.setPrivateKey(id, "key")
        registry.setKeyPassphrase(id, "phrase")
        registry.setPassword(id, "password")

        registry.delete(id)

        assertTrue(registry.list().isEmpty())
        assertFalse(secrets.hasSecret("ssh-private-key:$id"))
        assertFalse(secrets.hasSecret("ssh-key-passphrase:$id"))
        assertFalse(secrets.hasSecret("ssh-password:$id"))
    }

    @Test
    fun persistedJsonContainsNoSecrets() {
        val id = registry.add(host())
        registry.setPrivateKey(id, "PRIVATE-KEY-MATERIAL-12345")
        registry.setPassword(id, "PASSWORD-MATERIAL-67890")

        val raw = store.raw[SshHostRegistry.STORE_KEY]
        assertNotNull(raw)
        assertFalse(raw!!.contains("PRIVATE-KEY-MATERIAL-12345"))
        assertFalse(raw.contains("PASSWORD-MATERIAL-67890"))
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
