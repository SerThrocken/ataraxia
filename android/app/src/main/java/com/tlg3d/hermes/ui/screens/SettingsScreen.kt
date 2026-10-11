package com.tlg3d.hermes.ui.screens

import android.app.Application
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import com.tlg3d.hermes.HermesApp
import com.tlg3d.hermes.data.GatewayPrefs
import com.tlg3d.hermes.network.GatewayConfig
import com.tlg3d.hermes.network.GatewayException
import com.tlg3d.hermes.network.OkHttpGatewayClient
import kotlinx.coroutines.launch

class SettingsViewModel(app: Application) : AndroidViewModel(app) {
    private val hermesApp = app as HermesApp
    private val prefs = hermesApp.gatewayPrefs

    var host by mutableStateOf(prefs.host)
    var apiKey by mutableStateOf("")
    var status by mutableStateOf<String?>(null)
    var testing by mutableStateOf(false)

    fun save() {
        prefs.host = host
        val key = apiKey.trim()
        if (key.isNotEmpty()) {
            hermesApp.secretStore.saveApiKey(GatewayPrefs.API_KEY_ALIAS, key)
            apiKey = ""
            status = "Saved. Key is in the Android Keystore."
        } else {
            status = "Saved host."
        }
    }

    fun testConnection() {
        val key = apiKey.trim().ifEmpty {
            hermesApp.secretStore.getApiKey(GatewayPrefs.API_KEY_ALIAS).orEmpty()
        }
        if (key.isBlank()) {
            status = "Enter the API key first."
            return
        }
        testing = true
        status = null
        viewModelScope.launch {
            try {
                val client = OkHttpGatewayClient(GatewayConfig.forHost(host.trim(), key))
                val ok = client.health()
                if (!ok) {
                    status = "Gateway unreachable at ${host.trim()}."
                } else {
                    val models = client.listModels()
                    status = "Connected. ${models.size} model(s): " +
                        models.joinToString { it.id } + "."
                }
            } catch (e: GatewayException) {
                status = "Failed: ${e.message}"
            } catch (e: Exception) {
                status = "Failed: ${e.message ?: e.javaClass.simpleName}"
            } finally {
                testing = false
            }
        }
    }
}

/** Gateway connection: host + API key (Keystore), with a live connection test. */
@Composable
fun SettingsScreen(vm: SettingsViewModel = viewModel()) {
    val context = LocalContext.current
    // Warm the Keystore on first composition so saves don't block later.
    remember { (context.applicationContext as HermesApp).secretStore }
    var keySaved by remember { mutableStateOf(false) }

    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Top,
    ) {
        Text("Gateway", style = MaterialTheme.typography.headlineMedium)
        Spacer(Modifier.height(16.dp))
        OutlinedTextField(
            value = vm.host,
            onValueChange = { vm.host = it },
            label = { Text("Gateway host") },
            supportingText = { Text("Tailscale IP or hostname; port 8642 is assumed.") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
                .semantics { contentDescription = "Gateway host" },
        )
        Spacer(Modifier.height(12.dp))
        OutlinedTextField(
            value = vm.apiKey,
            onValueChange = { vm.apiKey = it; keySaved = false },
            label = { Text("API key") },
            supportingText = { Text("Stored only in the Android Keystore, never in plain text.") },
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
            singleLine = true,
            modifier = Modifier.fillMaxWidth()
                .semantics { contentDescription = "API key, hidden" },
        )
        Spacer(Modifier.height(16.dp))
        Button(
            onClick = {
                vm.save()
                keySaved = true
            },
            modifier = Modifier.semantics { contentDescription = "Save gateway settings" },
        ) {
            Text("Save")
        }
        Spacer(Modifier.height(8.dp))
        Button(
            onClick = { vm.testConnection() },
            enabled = !vm.testing,
            modifier = Modifier.semantics { contentDescription = "Test gateway connection" },
        ) {
            if (vm.testing) {
                CircularProgressIndicator(
                    modifier = Modifier.size(18.dp),
                    strokeWidth = 2.dp,
                )
            } else {
                Text("Test connection")
            }
        }
        vm.status?.let {
            Spacer(Modifier.height(16.dp))
            Text(
                it,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.semantics { contentDescription = "Connection status: $it" },
            )
        }
        if (keySaved) {
            Spacer(Modifier.height(8.dp))
            Text(
                "The key field is cleared after saving; the stored key stays in the Keystore.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}
