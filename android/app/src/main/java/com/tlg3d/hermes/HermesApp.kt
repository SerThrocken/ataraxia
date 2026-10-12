package com.tlg3d.hermes

import android.app.Application
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import com.tlg3d.hermes.data.GatewayConnectionRegistry
import com.tlg3d.hermes.data.GatewayPrefs
import com.tlg3d.hermes.data.KeyValueStore
import com.tlg3d.hermes.data.SharedPrefsKeyValueStore
import com.tlg3d.hermes.network.GatewayClient
import com.tlg3d.hermes.network.GatewayConfig
import com.tlg3d.hermes.network.OkHttpGatewayClient
import com.tlg3d.hermes.security.KeystoreSecretStore
import com.tlg3d.hermes.security.SecretStore
import com.tlg3d.hermes.ssh.PrefsSshHostKeyTrust
import com.tlg3d.hermes.ssh.SshHostKeyTrust
import com.tlg3d.hermes.ssh.SshHostRegistry
import com.tlg3d.hermes.ssh.SshjSessionFactory
import com.tlg3d.hermes.widget.WidgetRefreshWorker
import java.util.concurrent.TimeUnit

/** App-wide singletons, wired manually (no DI framework at this stage). */
class HermesApp : Application() {
    val secretStore: SecretStore by lazy { KeystoreSecretStore(this) }
    val gatewayPrefs: GatewayPrefs by lazy { GatewayPrefs(this) }

    /**
     * Phase 4 (Worker 4): multi-gateway connection registry + SSH host
     * registry. First access migrates the legacy single connection
     * (GatewayPrefs.host + API_KEY_ALIAS) into the registry as its first
     * entry — import, never orphan. Secrets stay in the Keystore under
     * per-entry aliases; the registry JSON holds none.
     */
    val registryStore: KeyValueStore by lazy {
        SharedPrefsKeyValueStore(getSharedPreferences(REGISTRY_PREFS, MODE_PRIVATE))
    }
    val connectionRegistry: GatewayConnectionRegistry by lazy {
        GatewayConnectionRegistry(registryStore, secretStore).also {
            it.migrateFromLegacy(
                gatewayPrefs.host,
                GatewayPrefs.API_KEY_ALIAS,
                gatewayPrefs.hostExplicitlySet,
            )
        }
    }
    val sshHostRegistry: SshHostRegistry by lazy {
        SshHostRegistry(registryStore, secretStore)
    }
    val sshHostKeyTrust: SshHostKeyTrust by lazy {
        PrefsSshHostKeyTrust(registryStore)
    }
    val sshSessionFactory: SshjSessionFactory by lazy {
        SshjSessionFactory(secretStore, sshHostKeyTrust)
    }

    override fun onCreate() {
        super.onCreate()
        // Gateway-status widget refresh: 15 min is the Android minimum for
        // periodic work. KEEP policy so reinstalls don't stack schedules.
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            WIDGET_REFRESH_WORK,
            ExistingPeriodicWorkPolicy.KEEP,
            PeriodicWorkRequestBuilder<WidgetRefreshWorker>(15, TimeUnit.MINUTES).build(),
        )
    }

    /**
     * Builds a client for the active registry connection (key from the
     * Keystore under the connection's alias). Returns null when no API key
     * has been saved yet (Settings prompts for it).
     *
     * Switching gateways is `connectionRegistry.setActiveId(id)`: clients
     * are built per call, so the next call picks up the new gateway —
     * nothing long-lived to tear down, no cross-talk between gateways.
     * The legacy single-connection path below only runs when the registry
     * is empty and was never migrated.
     */
    fun gatewayClient(): GatewayClient? {
        val active = connectionRegistry.getActive()
        if (active != null) {
            val key = connectionRegistry.getApiKey(active.id)?.takeIf { it.isNotBlank() }
                ?: return null
            return OkHttpGatewayClient(GatewayConfig(baseUrl = active.baseUrl, apiKey = key))
        }
        val key = secretStore.getApiKey(GatewayPrefs.API_KEY_ALIAS)?.takeIf { it.isNotBlank() }
            ?: return null
        return OkHttpGatewayClient(GatewayConfig.forHost(gatewayPrefs.host, key))
    }

    companion object {
        const val WIDGET_REFRESH_WORK = "gateway-widget-refresh"
        const val REGISTRY_PREFS = "hermes_registry"
    }
}
