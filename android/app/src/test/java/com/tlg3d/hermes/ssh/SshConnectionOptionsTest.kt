package com.tlg3d.hermes.ssh

import com.tlg3d.hermes.testutil.InMemoryKeyValueStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * SSH option building/parsing without live connections: pure validation
 * and string handling, no sockets, no keys.
 */
class SshConnectionOptionsTest {

    @Test
    fun fromHostMapsFields() {
        val host = SshHost(
            id = "abc", name = "Acer", host = "100.101.250.118",
            port = 2222, username = "tlcro", authKind = SshAuthKind.PASSWORD,
        )

        val options = SshConnectionOptions.fromHost(host)

        assertEquals("100.101.250.118", options.host)
        assertEquals(2222, options.port)
        assertEquals("tlcro", options.username)
        assertEquals(SshAuthKind.PASSWORD, options.authKind)
    }

    @Test
    fun validateReturnsNullWhenValid() {
        val options = SshConnectionOptions(host = "100.101.250.118", port = 22, username = "tlcro")

        assertNull(options.validate())
    }

    @Test
    fun validateRejectsBadHost() {
        listOf("", "   ", "not a host!", "http://example.com").forEach { bad ->
            val options = SshConnectionOptions(host = bad, port = 22, username = "tlcro")
            assertNotNull("host='$bad'", options.validate())
        }
    }

    @Test
    fun validateRejectsBadPort() {
        listOf(0, -1, 65536).forEach { port ->
            val options = SshConnectionOptions(host = "example.com", port = port, username = "tlcro")
            assertNotNull("port=$port", options.validate())
        }
    }

    @Test
    fun validateRejectsEmptyUsername() {
        listOf("", "   ").forEach { username ->
            val options = SshConnectionOptions(host = "example.com", port = 22, username = username)
            assertNotNull("username='$username'", options.validate())
        }
    }

    @Test
    fun describeFormatsUserAtHostColonPort() {
        val options = SshConnectionOptions(host = "100.101.250.118", port = 2222, username = "tlcro")

        assertEquals("tlcro@100.101.250.118:2222", options.describe())
    }

    @Test
    fun fingerprintIsStableOpenSshFormat() {
        // SHA256:base64, no padding — the format both platforms pin.
        val a = SshjTerminalSession.fingerprint(byteArrayOf(1, 2, 3))
        val b = SshjTerminalSession.fingerprint(byteArrayOf(1, 2, 3))

        assertEquals(a, b)
        assertTrue(a.startsWith("SHA256:"))
        assertFalse(a.contains("="))
    }

    @Test
    fun hostKeyTrustRoundTripsPin() {
        val store = InMemoryKeyValueStore()
        val trust = PrefsSshHostKeyTrust(store)

        assertNull(trust.getPinnedFingerprint("example.com", 22))
        trust.pinFingerprint("Example.COM", 22, "SHA256:abc123")
        assertEquals("SHA256:abc123", trust.getPinnedFingerprint("example.com", 22))
        // Host lookup is case-insensitive; ports are distinct.
        assertNull(trust.getPinnedFingerprint("example.com", 2222))
    }
}
