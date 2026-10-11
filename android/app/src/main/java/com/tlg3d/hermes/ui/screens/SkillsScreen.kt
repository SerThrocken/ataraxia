package com.tlg3d.hermes.ui.screens

import android.app.Application
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
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
import com.tlg3d.hermes.network.SkillInfo
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class SkillsViewModel(app: Application) : AndroidViewModel(app) {
    private val hermesApp = app as HermesApp
    private var client: GatewayClient? = null

    var skills = mutableStateListOf<SkillInfo>()
        private set
    var loading by mutableStateOf(false)
        private set
    var error by mutableStateOf<String?>(null)
        private set
    var needsKey by mutableStateOf(false)
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
                val list = withContext(Dispatchers.IO) { c.listSkills() }
                skills.clear()
                skills.addAll(list)
            } catch (e: Exception) {
                error = describeGatewayError(e)
            } finally {
                loading = false
            }
        }
    }

    fun clearError() {
        error = null
    }
}

@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
@Composable
fun SkillsScreen() {
    val app = LocalContext.current.applicationContext as HermesApp
    val vm: SkillsViewModel = viewModel(
        factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                SkillsViewModel(app) as T
        },
    )

    LaunchedEffect(Unit) { vm.connect() }

    if (vm.needsKey) {
        PlaceholderScreen(
            title = "Skills",
            body = "No API key saved yet — add it in Settings, then come back here.",
        )
        return
    }

    Column(Modifier.fillMaxSize()) {
        ScreenTitle("Skills")
        // Honest label: the gateway API is list-only. Never imply install/toggle.
        Text(
            "Skills installed on the gateway. The gateway API only lists skills — " +
                "there is no install, enable/disable, or content endpoint — " +
                "so this screen is a read-only browser.",
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
                .semantics { contentDescription = "Skill list. Pull to refresh." },
        ) {
            when {
                vm.loading && vm.skills.isEmpty() -> LoadingState("Loading skills…")
                vm.skills.isEmpty() -> EmptyState(
                    "No skills returned",
                    "The gateway's skills endpoint currently fails server-side " +
                        "(HTTP 500, verified 2026-10-11), so an empty list here " +
                        "may reflect the server rather than your setup.",
                )
                else -> LazyColumn(Modifier.fillMaxSize()) {
                    items(vm.skills, key = { it.id ?: it.name }) { s ->
                        SkillRow(s)
                    }
                }
            }
        }
    }
}

@Composable
private fun SkillRow(skill: SkillInfo) {
    Column(
        modifier = Modifier.fillMaxWidth()
            .padding(horizontal = 16.dp, vertical = 12.dp)
            .semantics {
                contentDescription =
                    "Skill ${skill.name}" +
                        (skill.description?.ifBlank { null }?.let { ": $it" } ?: "")
            },
    ) {
        Text(skill.name, style = MaterialTheme.typography.titleMedium, maxLines = 1)
        skill.category?.ifBlank { null }?.let {
            Text(
                it,
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.tertiary,
            )
        }
        Text(
            skill.description?.ifBlank { null } ?: "No description provided.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}
