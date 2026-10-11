package com.tlg3d.hermes.ui.screens

import android.app.Application
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
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
import com.tlg3d.hermes.network.Session
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class SessionsViewModel(app: Application) : AndroidViewModel(app) {
    private val hermesApp = app as HermesApp
    private var client: GatewayClient? = null

    var sessions = mutableStateListOf<Session>()
        private set
    var loading by mutableStateOf(false)
        private set
    var error by mutableStateOf<String?>(null)
        private set
    var needsKey by mutableStateOf(false)
        private set
    var pendingDelete by mutableStateOf<Session?>(null)
    var showCreate by mutableStateOf(false)

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
                val list = withContext(Dispatchers.IO) { c.listSessions() }
                sessions.clear()
                sessions.addAll(list)
            } catch (e: Exception) {
                error = describeGatewayError(e)
            } finally {
                loading = false
            }
        }
    }

    fun createSession(title: String?, onCreated: (String) -> Unit) {
        val c = client ?: return
        loading = true
        error = null
        viewModelScope.launch {
            try {
                val s = withContext(Dispatchers.IO) { c.createSession(title) }
                sessions.add(0, s)
                onCreated(s.id)
            } catch (e: Exception) {
                error = describeGatewayError(e)
            } finally {
                loading = false
            }
        }
    }

    fun deleteSession(id: String) {
        val c = client ?: return
        pendingDelete = null
        viewModelScope.launch {
            try {
                val ok = withContext(Dispatchers.IO) { c.deleteSession(id) }
                if (ok) {
                    sessions.removeAll { it.id == id }
                } else {
                    error = "The gateway did not confirm deletion."
                }
            } catch (e: Exception) {
                error = describeGatewayError(e)
            }
        }
    }

    fun clearError() {
        error = null
    }
}

@OptIn(ExperimentalFoundationApi::class, androidx.compose.material3.ExperimentalMaterial3Api::class)
@Composable
fun SessionsScreen(onOpenSession: (String) -> Unit) {
    val app = LocalContext.current.applicationContext as HermesApp
    val vm: SessionsViewModel = viewModel(
        factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                SessionsViewModel(app) as T
        },
    )

    LaunchedEffect(Unit) { vm.connect() }

    if (vm.needsKey) {
        PlaceholderScreen(
            title = "Sessions",
            body = "No API key saved yet — add it in Settings, then come back here.",
        )
        return
    }

    Scaffold(
        floatingActionButton = {
            FloatingActionButton(
                onClick = { vm.showCreate = true },
                modifier = Modifier.semantics { contentDescription = "Create session" },
            ) {
                Icon(Icons.Filled.Add, contentDescription = null)
            }
        },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            ScreenTitle("Sessions")
            vm.error?.let { ErrorBar(it, vm::clearError) }
            PullToRefreshBox(
                isRefreshing = vm.loading,
                onRefresh = { vm.refresh() },
                modifier = Modifier.fillMaxSize()
                    .semantics { contentDescription = "Session list. Pull to refresh." },
            ) {
                when {
                    vm.loading && vm.sessions.isEmpty() -> LoadingState("Loading sessions…")
                    vm.sessions.isEmpty() ->
                        if (vm.error != null) {
                            EmptyState(
                                "Couldn't load sessions",
                                "Pull to retry, or check the gateway connection in Settings.",
                            )
                        } else {
                            EmptyState(
                                "No sessions yet",
                                "Start one with the + button below.",
                            )
                        }
                    else -> LazyColumn(Modifier.fillMaxSize()) {
                        items(vm.sessions, key = { it.id }) { s ->
                            SessionRow(
                                session = s,
                                onOpen = { onOpenSession(s.id) },
                                onDelete = { vm.pendingDelete = s },
                            )
                        }
                    }
                }
            }
        }
    }

    vm.pendingDelete?.let { s ->
        val title = s.title?.ifBlank { null } ?: "Untitled"
        AlertDialog(
            onDismissRequest = { vm.pendingDelete = null },
            title = { Text("Delete session?") },
            text = { Text("“$title” will be permanently deleted. This cannot be undone.") },
            confirmButton = {
                TextButton(
                    onClick = { vm.deleteSession(s.id) },
                    modifier = Modifier.semantics {
                        contentDescription = "Confirm delete session $title"
                    },
                ) { Text("Delete") }
            },
            dismissButton = {
                TextButton(onClick = { vm.pendingDelete = null }) { Text("Cancel") }
            },
        )
    }

    if (vm.showCreate) {
        var title by remember { mutableStateOf("") }
        AlertDialog(
            onDismissRequest = { vm.showCreate = false },
            title = { Text("New session") },
            text = {
                OutlinedTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = { Text("Title (optional)") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth()
                        .semantics { contentDescription = "Session title, optional" },
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    vm.showCreate = false
                    vm.createSession(title.ifBlank { null }, onOpenSession)
                }) { Text("Create") }
            },
            dismissButton = {
                TextButton(onClick = { vm.showCreate = false }) { Text("Cancel") }
            },
        )
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun SessionRow(
    session: Session,
    onOpen: () -> Unit,
    onDelete: () -> Unit,
) {
    val title = session.title?.ifBlank { null } ?: "Untitled"
    Row(
        modifier = Modifier.fillMaxWidth()
            .combinedClickable(onClick = onOpen, onLongClick = onDelete)
            .padding(horizontal = 16.dp, vertical = 12.dp)
            .semantics {
                contentDescription =
                    "Session $title, ${session.messageCount} messages, " +
                        "last active ${relativeTime(session.lastActive ?: session.startedAt)}"
            },
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleMedium, maxLines = 1)
            Text(
                "${session.messageCount} messages · " +
                    relativeTime(session.lastActive ?: session.startedAt) +
                    (session.model?.let { " · $it" } ?: ""),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
            )
        }
        IconButton(
            onClick = onDelete,
            modifier = Modifier.semantics {
                contentDescription = "Delete session $title"
            },
        ) {
            Icon(Icons.Filled.Delete, contentDescription = null)
        }
    }
}
