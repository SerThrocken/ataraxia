package com.tlg3d.hermes.ui.screens

import android.app.Application
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import com.tlg3d.hermes.HermesApp
import com.tlg3d.hermes.network.GatewayClient
import com.tlg3d.hermes.network.ModelInfo
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class ModelsViewModel(app: Application) : AndroidViewModel(app) {
    private val hermesApp = app as HermesApp
    private val prefs = hermesApp.gatewayPrefs
    private var client: GatewayClient? = null

    var models = mutableStateListOf<ModelInfo>()
        private set
    var loading by mutableStateOf(false)
        private set
    var error by mutableStateOf<String?>(null)
        private set
    var needsKey by mutableStateOf(false)
        private set
    var defaultModelId by mutableStateOf(prefs.defaultModel)
        private set

    fun connect() {
        val c = hermesApp.gatewayClient()
        if (c == null) {
            needsKey = true
            return
        }
        client = c
        refresh()
    }

    fun refresh() {
        val c = client ?: return
        loading = true
        error = null
        viewModelScope.launch {
            try {
                val list = withContext(Dispatchers.IO) { c.listModels() }
                models.clear()
                models.addAll(list)
            } catch (e: Exception) {
                error = describeGatewayError(e)
            } finally {
                loading = false
            }
        }
    }

    /** Persist the default model on this device. Tap the default again to clear it. */
    fun toggleDefault(id: String) {
        val next = if (defaultModelId == id) null else id
        prefs.defaultModel = next
        defaultModelId = next
    }

    fun clearError() {
        error = null
    }
}

@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
@Composable
fun ModelsScreen() {
    val app = LocalContext.current.applicationContext as HermesApp
    val vm: ModelsViewModel = viewModel(
        factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                ModelsViewModel(app) as T
        },
    )

    LaunchedEffect(Unit) { vm.connect() }

    if (vm.needsKey) {
        PlaceholderScreen(
            title = "Models",
            body = "No API key saved yet — add it in Settings, then come back here.",
        )
        return
    }

    Column(Modifier.fillMaxSize()) {
        ScreenTitle("Models")
        Text(
            "Tap a model to make it this device's default; it is shown in Chat. " +
                "The gateway currently exposes a single virtual model, and chat " +
                "requests in the current API carry no model field.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.fillMaxWidth()
                .padding(horizontal = 16.dp)
                .padding(bottom = 8.dp),
        )
        vm.error?.let { ErrorBar(it, vm::clearError) }
        PullToRefreshBox(
            isRefreshing = vm.loading,
            onRefresh = { vm.refresh() },
            modifier = Modifier.fillMaxSize()
                .semantics { contentDescription = "Model list. Pull to refresh." },
        ) {
            when {
                vm.loading && vm.models.isEmpty() -> LoadingState("Loading models…")
                vm.models.isEmpty() -> EmptyState(
                    "No models returned",
                    "The gateway listed no models. Pull to retry.",
                )
                else -> LazyColumn(Modifier.fillMaxSize()) {
                    items(vm.models, key = { it.id }) { m ->
                        ModelRow(
                            model = m,
                            isDefault = vm.defaultModelId == m.id,
                            onSelect = { vm.toggleDefault(m.id) },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ModelRow(
    model: ModelInfo,
    isDefault: Boolean,
    onSelect: () -> Unit,
) {
    val name = model.name?.ifBlank { null } ?: model.id
    Row(
        modifier = Modifier.fillMaxWidth()
            .clickable(onClick = onSelect)
            .padding(horizontal = 16.dp, vertical = 12.dp)
            .semantics {
                contentDescription =
                    "Model $name" +
                        (model.provider?.let { ", provider $it" } ?: "") +
                        if (isDefault) ", default model" else ""
            },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            Text(name, style = MaterialTheme.typography.titleMedium, maxLines = 1)
            Text(
                (model.provider ?: "unknown provider") +
                    if (model.name != null && model.name != model.id) " · ${model.id}" else "",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
            )
        }
        RadioButton(
            selected = isDefault,
            onClick = onSelect,
            modifier = Modifier.semantics {
                contentDescription = if (isDefault) "Default model" else "Set as default model"
            },
        )
    }
}
