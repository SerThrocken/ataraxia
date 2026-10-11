package com.tlg3d.hermes.verify

import com.tlg3d.hermes.network.GatewayConfig
import com.tlg3d.hermes.network.OkHttpGatewayClient
import com.tlg3d.hermes.network.StreamEvent
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.URI
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.runBlocking
import okhttp3.Credentials
import okhttp3.OkHttpClient

/**
 * Live-gateway protocol verification. Exercises the REAL [OkHttpGatewayClient]
 * (the same class the app uses) against the gateway over the Tailscale tunnel.
 *
 * Debug-only: lives in `src/debug`, never in release builds.
 *
 * Environment:
 * - `HERMES_TEST_API_KEY` — gateway key (required; never printed)
 * - `HERMES_TEST_HOST`    — gateway host (default 100.101.250.118)
 * - `TS_PROXY`            — CONNECT tunnel proxy, `http://user:pass@host:port`
 *
 * Exit code 0 = all checks passed, 1 = any failure.
 */
fun main(): Unit = runBlocking {
    val key = System.getenv("HERMES_TEST_API_KEY")?.takeIf { it.isNotBlank() }
    val host = System.getenv("HERMES_TEST_HOST") ?: "100.101.250.118"
    if (key == null) {
        println("FAIL: HERMES_TEST_API_KEY is not set")
        kotlin.system.exitProcess(1)
    }
    val client = OkHttpGatewayClient(GatewayConfig.forHost(host, key), tunneledClient())
    var failures = 0

    suspend fun check(name: String, block: suspend () -> Unit) {
        try {
            block()
            println("ok - $name")
        } catch (e: Exception) {
            failures++
            println("FAIL - $name: ${e.message ?: e.javaClass.simpleName}")
        }
    }

    check("health") {
        check(client.health()) { "GET /health did not return 200" }
    }

    check("listModels") {
        val models = client.listModels()
        check(models.isNotEmpty()) { "no models returned" }
        println("     models: ${models.joinToString { it.id }}")
    }

    check("listSessions") {
        val sessions = client.listSessions(5)
        println("     sessions: ${sessions.size}")
    }

    check("listSkills (500 tolerated)") {
        val skills = client.listSkills()
        println("     skills: ${skills.size} (gateway 500 bug tolerated)")
    }

    var sessionId = ""
    check("createSession") {
        val s = client.createSession("android-verify")
        check(s.id.isNotBlank()) { "empty session id" }
        sessionId = s.id
        println("     id length: ${s.id.length}")
    }

    check("streamChat") {
        val deltas = StringBuilder()
        var final: String? = null
        var approvals = 0
        client.streamChat(sessionId, "reply with the word ok") { event ->
            when (event) {
                is StreamEvent.Delta -> deltas.append(event.text)
                is StreamEvent.Completed -> final = event.content
                is StreamEvent.Approval -> approvals++
                is StreamEvent.Failed -> throw IllegalStateException(event.message)
                is StreamEvent.Reasoning -> Unit
            }
        }
        check(approvals == 0) { "unexpected approval request" }
        val content = final ?: deltas.toString()
        println("     reply: ${content.take(80)}")
        check(content.contains("ok", ignoreCase = true)) { "reply did not contain 'ok': ${content.take(80)}" }
    }

    check("sendChat") {
        val msg = client.sendChat(sessionId, "reply with the word yes")
        println("     reply: ${msg.content.take(80)}")
        check(msg.content.contains("yes", ignoreCase = true)) {
            "reply did not contain 'yes': ${msg.content.take(80)}"
        }
    }

    check("getMessages") {
        val msgs = client.getMessages(sessionId)
        val roles = msgs.map { it.role }.toSet()
        check(msgs.size >= 4 && "user" in roles && "assistant" in roles) {
            "expected user+assistant history, got ${msgs.size} messages"
        }
        println("     messages: ${msgs.size}")
    }

    check("deleteSession") {
        check(client.deleteSession(sessionId)) { "delete returned false" }
    }

    if (failures == 0) println("VERIFY PASS") else println("VERIFY FAIL ($failures)")
    kotlin.system.exitProcess(if (failures == 0) 0 else 1)
}

/** OkHttp through a manual CONNECT tunnel (Tailscale is not routable from this VM).
 *
 * The egress proxy only speaks CONNECT (like ts-proxy.py); it does not do
 * plain forward-proxying, so OkHttp's built-in proxy support can't be used.
 * This factory hands OkHttp a socket whose connect() dials the proxy and
 * performs the CONNECT handshake itself.
 */
private fun tunneledClient(): OkHttpClient {
    val raw = System.getenv("TS_PROXY") ?: error("TS_PROXY is not set")
    val uri = URI(raw)
    val userInfo = (uri.userInfo ?: "").split(":", limit = 2)
    val credential = Credentials.basic(userInfo.getOrElse(0) { "" }, userInfo.getOrElse(1) { "" })
    // The proxy's IPv4 listener resets CONNECT handshakes; IPv6 works.
    // (Pure this-VM artifact — the phone reaches the gateway over tailnet directly.)
    val proxyIp = java.net.InetAddress.getAllByName(uri.host)
        .firstOrNull { it is java.net.Inet6Address }
        ?: java.net.InetAddress.getByName(uri.host)
    val proxyAddr = InetSocketAddress(proxyIp, uri.port)
    val targetHost = System.getenv("HERMES_TEST_HOST") ?: "100.101.250.118"
    val targetPort = 8642

    val factory = object : javax.net.SocketFactory() {
        override fun createSocket(): java.net.Socket = object : java.net.Socket() {
            override fun connect(endpoint: java.net.SocketAddress?, timeout: Int) {
                super.connect(proxyAddr, timeout)
                val out = getOutputStream()
                val handshake = "CONNECT $targetHost:$targetPort HTTP/1.1\r\n" +
                    "Host: $targetHost:$targetPort\r\n" +
                    "Proxy-Authorization: $credential\r\n\r\n"
                out.write(handshake.toByteArray(Charsets.US_ASCII))
                out.flush()
                val status = readAsciiLine(getInputStream())
                    ?: throw java.io.IOException("proxy closed the CONNECT handshake")
                if (!status.contains(" 200")) {
                    throw java.io.IOException("proxy CONNECT failed: $status")
                }
                var line: String?
                do {
                    line = readAsciiLine(getInputStream())
                } while (line != null && line.isNotEmpty())
            }
        }

        override fun createSocket(host: String?, port: Int) = createSocket()
        override fun createSocket(host: String?, port: Int, localHost: java.net.InetAddress?, localPort: Int) = createSocket()
        override fun createSocket(host: java.net.InetAddress?, port: Int) = createSocket()
        override fun createSocket(host: java.net.InetAddress?, port: Int, localHost: java.net.InetAddress?, localPort: Int) = createSocket()
    }

    return OkHttpClient.Builder()
        .socketFactory(factory)
        .proxy(Proxy.NO_PROXY)
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(90, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .build()
}

/** Reads one CRLF-terminated ASCII line without buffering past the newline. */
private fun readAsciiLine(input: java.io.InputStream): String? {
    val sb = StringBuilder()
    while (true) {
        val b = input.read()
        if (b < 0) return if (sb.isEmpty()) null else sb.toString()
        if (b == '\n'.code) break
        if (b != '\r'.code) sb.append(b.toChar())
    }
    return sb.toString()
}
