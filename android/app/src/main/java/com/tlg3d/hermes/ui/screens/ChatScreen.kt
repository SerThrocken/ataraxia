package com.tlg3d.hermes.ui.screens

import android.app.Application
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.ExpandLess
import androidx.compose.material.icons.filled.ExpandMore
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import com.tlg3d.hermes.HermesApp
import com.tlg3d.hermes.network.ApprovalRequest
import com.tlg3d.hermes.network.ChatImageAttachment
import com.tlg3d.hermes.network.GatewayClient
import com.tlg3d.hermes.network.Session
import com.tlg3d.hermes.network.StreamEvent
import com.tlg3d.hermes.share.SharePayload
import java.io.ByteArrayOutputStream
import kotlin.math.max
import com.tlg3d.hermes.runs.trackActiveRun
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** One row in the chat transcript. Public so the ViewModel can expose it. */
data class UiMessage(
    val key: String,
    val role: String,
    val content: String,
    val pending: Boolean = false,
)

/** A shared/picked image: bitmap preview for the composer + gateway payload. */
data class AttachedImage(
    val preview: Bitmap,
    val attachment: ChatImageAttachment,
    val source: String,
)

class ChatViewModel(app: Application) : AndroidViewModel(app) {
    private val hermesApp = app as HermesApp
    private var client: GatewayClient? = null

    var sessions = mutableStateListOf<Session>()
        private set
    var selectedSessionId by mutableStateOf<String?>(null)
        private set
    var messages = mutableStateListOf<UiMessage>()
        private set
    var input by mutableStateOf("")
    var streaming by mutableStateOf(false)
        private set
    var loading by mutableStateOf(false)
        private set
    var error by mutableStateOf<String?>(null)
        private set
    var pendingApproval by mutableStateOf<ApprovalRequest?>(null)
        private set
    var reasoningText by mutableStateOf("")
        private set
    var reasoningExpanded by mutableStateOf(false)
    var needsKey by mutableStateOf(false)
        private set
    var defaultModel by mutableStateOf<String?>(null)
        private set
    var attachedImages = mutableStateListOf<AttachedImage>()
        private set

    private val prefs get() = hermesApp.gatewayPrefs
    private var preselectedId: String? = null
    private var messagesFor: String? = null
    private var pendingShare: SharePayload? = null

    private var streamJob: Job? = null

    fun connect() {
        val c = hermesApp.gatewayClient()
        if (c == null) {
            needsKey = true
            return
        }
        client = c
        refreshDefaultModel()
        preselectedId?.let {
            selectedSessionId = it
            preselectedId = null
        }
        refreshSessions()
        pendingShare?.let {
            pendingShare = null
            startSharedChat(c, it)
        }
    }

    /** Re-read the saved default model (it may have changed on the Models tab). */
    fun refreshDefaultModel() {
        defaultModel = prefs.defaultModel
    }

    /** Open a specific session, connecting first when the client isn't ready. */
    fun openSession(id: String) {
        if (client == null) {
            preselectedId = id
            connect()
            return
        }
        if (id == selectedSessionId && id == messagesFor) return
        selectedSessionId = id
        refreshSessions()
    }

    fun refreshSessions() {
        val c = client ?: return
        loading = true
        error = null
        viewModelScope.launch {
            try {
                val list = withContext(Dispatchers.IO) { c.listSessions() }
                sessions.clear()
                sessions.addAll(list)
                val target = selectedSessionId?.takeIf { sid -> list.any { it.id == sid } }
                    ?: list.firstOrNull()?.id
                if (target != null && target != messagesFor) selectSession(target)
            } catch (e: Exception) {
                error = describeGatewayError(e)
            } finally {
                loading = false
            }
        }
    }

    fun selectSession(id: String) {
        val c = client ?: return
        selectedSessionId = id
        pendingApproval = null
        reasoningText = ""
        loading = true
        viewModelScope.launch {
            try {
                val history = withContext(Dispatchers.IO) { c.getMessages(id) }
                messages.clear()
                messages.addAll(history.mapIndexed { i, m ->
                    UiMessage(m.id ?: "m$i", m.role, m.content)
                })
                messagesFor = id
            } catch (e: Exception) {
                error = describeGatewayError(e)
            } finally {
                loading = false
            }
        }
    }

    fun newChat() {
        val c = client ?: return
        loading = true
        viewModelScope.launch {
            try {
                val s = withContext(Dispatchers.IO) { c.createSession("Android chat") }
                sessions.add(0, s)
                selectSession(s.id)
            } catch (e: Exception) {
                error = describeGatewayError(e)
            } finally {
                loading = false
            }
        }
    }

    /**
     * Opens a fresh session for a share-sheet payload: shared text goes into
     * the composer (the user reviews and taps Send), shared images attach as
     * thumbnails. Arriving before [connect] finishes parks the payload until
     * the client exists.
     */
    fun applySharedContent(payload: SharePayload) {
        val c = client
        if (c == null) {
            pendingShare = payload
            connect()
            return
        }
        startSharedChat(c, payload)
    }

    private fun startSharedChat(c: GatewayClient, payload: SharePayload) {
        loading = true
        error = null
        viewModelScope.launch {
            try {
                val s = withContext(Dispatchers.IO) { c.createSession("Shared content") }
                sessions.add(0, s)
                selectedSessionId = s.id
                messagesFor = s.id
                messages.clear()
                pendingApproval = null
                reasoningText = ""
                input = payload.text.orEmpty()
                attachedImages.clear()
                if (payload.imageUriStrings.isNotEmpty()) {
                    attachedImages.addAll(
                        withContext(Dispatchers.IO) { loadSharedImages(payload.imageUriStrings) },
                    )
                }
            } catch (e: Exception) {
                error = describeGatewayError(e)
            } finally {
                loading = false
            }
        }
    }

    fun removeAttachedImage(image: AttachedImage) {
        attachedImages.remove(image)
    }

    /**
     * Reads shared image URIs into downscaled JPEG payloads. Images are
     * bounded to [SHARE_IMAGE_MAX_DIM] px so a share stays a reasonable
     * request body; at most [MAX_SHARE_IMAGES] are kept.
     */
    private fun loadSharedImages(uriStrings: List<String>): List<AttachedImage> {
        return uriStrings.take(MAX_SHARE_IMAGES).mapNotNull { raw ->
            runCatching {
                val uri = Uri.parse(raw)
                val bitmap = decodeBoundedBitmap(uri) ?: return@runCatching null
                val out = ByteArrayOutputStream()
                bitmap.compress(Bitmap.CompressFormat.JPEG, 85, out)
                AttachedImage(bitmap, ChatImageAttachment(out.toByteArray(), "image/jpeg"), raw)
            }.getOrNull()
        }
    }

    private fun decodeBoundedBitmap(uri: Uri): Bitmap? {
        val resolver = getApplication<Application>().contentResolver
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, bounds) }
        val maxDim = max(bounds.outWidth, bounds.outHeight)
        if (maxDim <= 0) return null
        var sample = 1
        while (maxDim / sample > SHARE_IMAGE_MAX_DIM) sample *= 2
        val opts = BitmapFactory.Options().apply { inSampleSize = sample }
        return resolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, opts) }
    }

    fun send() {
        val c = client ?: return
        val sessionId = selectedSessionId ?: return
        val text = input.trim()
        val images = attachedImages.toList()
        if ((text.isEmpty() && images.isEmpty()) || streaming) return
        input = ""
        attachedImages.clear()
        error = null
        reasoningText = ""
        val shown = text +
            if (images.isNotEmpty()) "\n[${images.size} image${if (images.size == 1) "" else "s"} attached]" else ""
        messages.add(UiMessage("u${messages.size}", "user", shown.ifEmpty { "[image]" }))
        messages.add(UiMessage("pending", "assistant", "", pending = true))
        streaming = true
        streamJob = viewModelScope.launch(Dispatchers.IO) {
            try {
                if (images.isEmpty()) c.streamChat(sessionId, text, ::onStreamEvent)
                else c.streamChatImages(
                    sessionId,
                    text,
                    images.map { it.attachment },
                    ::onStreamEvent,
                )
            } catch (e: CancellationException) {
                throw e // user hit Stop; not an error
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    dropPending()
                    error = describeGatewayError(e)
                    streaming = false
                }
            }
        }
    }

    private fun onStreamEvent(event: StreamEvent) {
        // streamChat invokes this from the IO thread; hop to Main for state.
        viewModelScope.launch {
            when (event) {
                is StreamEvent.Delta -> appendDelta(event.text)
                is StreamEvent.Reasoning -> reasoningText += event.text
                is StreamEvent.Completed -> {
                    finalizePending(event.content)
                    streaming = false
                }
                is StreamEvent.Approval -> {
                    pendingApproval = event.request
                    // The approval event carries the run id: raise the
                    // persistent Stop/Steer controls for this run.
                    trackActiveRun(hermesApp, event.request.runId)
                }
                is StreamEvent.Failed -> {
                    dropPending()
                    error = event.message
                    streaming = false
                }
            }
        }
    }

    fun resolveApproval(choice: String) {
        val c = client ?: return
        val req = pendingApproval ?: return
        pendingApproval = null
        viewModelScope.launch(Dispatchers.IO) {
            try {
                c.resolveApproval(req.runId, choice, req.requestId)
            } catch (e: Exception) {
                withContext(Dispatchers.Main) { error = describeGatewayError(e) }
            }
        }
    }

    fun stop() {
        streamJob?.cancel()
        streamJob = null
        dropPending()
        streaming = false
    }

    fun clearError() {
        error = null
    }

    private fun appendDelta(delta: String) {
        val i = messages.indexOfLast { it.pending }
        if (i >= 0) {
            val m = messages[i]
            messages[i] = m.copy(content = m.content + delta)
        }
    }

    private fun finalizePending(content: String) {
        val i = messages.indexOfLast { it.pending }
        if (i >= 0) {
            val m = messages[i]
            messages[i] = m.copy(content = content.ifEmpty { m.content }, pending = false)
        }
    }

    private fun dropPending() {
        messages.removeAll { it.pending }
    }

    companion object {
        /** Longest image edge after downscaling (keeps share payloads small). */
        const val SHARE_IMAGE_MAX_DIM = 1280

        /** Max images kept per share; the gateway also caps parts server-side. */
        const val MAX_SHARE_IMAGES = 4
    }

}

@Composable
fun ChatScreen(
    initialSessionId: String? = null,
    sharedContent: SharePayload? = null,
    onSharedConsumed: () -> Unit = {},
) {
    val app = LocalContext.current.applicationContext as HermesApp
    val vm: ChatViewModel = viewModel(
        factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                ChatViewModel(app) as T
        },
    )

    LaunchedEffect(Unit) { vm.connect() }
    LaunchedEffect(initialSessionId) {
        vm.refreshDefaultModel()
        if (initialSessionId != null) vm.openSession(initialSessionId)
    }
    // A share from another app opens a fresh session with the composer prefilled.
    LaunchedEffect(sharedContent) {
        if (sharedContent != null) {
            vm.applySharedContent(sharedContent)
            onSharedConsumed()
        }
    }

    if (vm.needsKey) {
        PlaceholderScreen(
            title = "Chat",
            body = "No API key saved yet — add it in Settings, then come back here.",
        )
        return
    }

    Column(Modifier.fillMaxSize()) {
        SessionBar(vm)
        vm.pendingApproval?.let { ApprovalCard(it, vm::resolveApproval) }
        MessageList(vm, Modifier.weight(1f))
        if (vm.reasoningText.isNotEmpty()) ReasoningCard(vm)
        vm.error?.let { ErrorBar(it, vm::clearError) }
        Composer(vm)
    }
}

@Composable
private fun SessionBar(vm: ChatViewModel) {
    var showPicker by remember { mutableStateOf(false) }
    val current = vm.sessions.firstOrNull { it.id == vm.selectedSessionId }

    Column(Modifier.fillMaxWidth()) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        TextButton(
            onClick = { showPicker = true },
            modifier = Modifier.weight(1f)
                .semantics { contentDescription = "Choose chat session" },
        ) {
            Text(
                current?.title?.ifBlank { null }
                    ?: current?.preview?.take(32)
                    ?: if (vm.loading) "Loading…" else "Choose a chat",
                maxLines = 1,
                style = MaterialTheme.typography.titleMedium,
            )
        }
        IconButton(
            onClick = { vm.newChat() },
            modifier = Modifier.size(48.dp)
                .semantics { contentDescription = "Start new chat" },
        ) {
            Icon(Icons.Filled.Add, contentDescription = null)
        }
    }
    val modelLabel = current?.model ?: vm.defaultModel
    if (modelLabel != null) {
        Text(
            "Model: $modelLabel",
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.padding(start = 16.dp, bottom = 4.dp)
                .semantics { contentDescription = "Active model $modelLabel" },
        )
    }
    }

    if (showPicker) {
        AlertDialog(
            onDismissRequest = { showPicker = false },
            title = { Text("Sessions") },
            text = {
                LazyColumn(Modifier.heightIn(max = 400.dp)) {
                    items(vm.sessions, key = { it.id }) { s ->
                        Row(
                            modifier = Modifier.fillMaxWidth()
                                .clickable {
                                    vm.selectSession(s.id)
                                    showPicker = false
                                }
                                .padding(vertical = 12.dp)
                                .semantics {
                                    contentDescription =
                                        "Session ${s.title ?: "untitled"}, ${s.messageCount} messages"
                                },
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Column(Modifier.weight(1f)) {
                                Text(
                                    s.title?.ifBlank { null } ?: "Untitled",
                                    style = MaterialTheme.typography.bodyLarge,
                                    maxLines = 1,
                                )
                                Text(
                                    "${s.messageCount} messages" +
                                        (s.model?.let { " · $it" } ?: ""),
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        }
                    }
                }
            },
            confirmButton = {
                TextButton(onClick = { showPicker = false }) { Text("Close") }
            },
        )
    }
}

@Composable
private fun MessageList(vm: ChatViewModel, modifier: Modifier = Modifier) {
    val state = rememberLazyListState()
    LaunchedEffect(vm.messages.size) {
        if (vm.messages.isNotEmpty()) state.animateScrollToItem(vm.messages.lastIndex)
    }
    LazyColumn(
        state = state,
        modifier = modifier.fillMaxWidth()
            .semantics { contentDescription = "Conversation" },
        verticalArrangement = Arrangement.spacedBy(8.dp),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(12.dp),
    ) {
        items(vm.messages, key = { it.key }) { m ->
            if (m.role == "user") {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                    Surface(
                        color = MaterialTheme.colorScheme.primaryContainer,
                        shape = MaterialTheme.shapes.large,
                        modifier = Modifier.fillMaxWidth(0.85f)
                            .semantics { contentDescription = "You said: ${m.content}" },
                    ) {
                        Text(
                            m.content,
                            modifier = Modifier.padding(12.dp),
                            style = MaterialTheme.typography.bodyLarge,
                        )
                    }
                }
            } else {
                Column(
                    Modifier.fillMaxWidth()
                        .semantics { contentDescription = "Assistant: ${m.content}" },
                ) {
                    if (m.pending && m.content.isEmpty()) {
                        CircularProgressIndicator(
                            modifier = Modifier.size(20.dp).padding(4.dp),
                        )
                    } else {
                        Text(m.content, style = MaterialTheme.typography.bodyLarge)
                    }
                }
            }
        }
    }
}

@Composable
private fun ReasoningCard(vm: ChatViewModel) {
    Card(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp),
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surfaceContainerLow,
        ),
    ) {
        Column {
            Row(
                modifier = Modifier.fillMaxWidth()
                    .clickable { vm.reasoningExpanded = !vm.reasoningExpanded }
                    .padding(12.dp)
                    .semantics {
                        contentDescription =
                            if (vm.reasoningExpanded) "Hide reasoning" else "Show reasoning"
                    },
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    "Reasoning",
                    style = MaterialTheme.typography.labelLarge,
                    modifier = Modifier.weight(1f),
                )
                Icon(
                    if (vm.reasoningExpanded) Icons.Filled.ExpandLess else Icons.Filled.ExpandMore,
                    contentDescription = null,
                )
            }
            if (vm.reasoningExpanded) {
                Text(
                    vm.reasoningText,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(horizontal = 12.dp)
                        .padding(bottom = 12.dp),
                )
            }
        }
    }
}

@Composable
private fun ApprovalCard(req: ApprovalRequest, onChoice: (String) -> Unit) {
    val choices = req.choices.ifEmpty { listOf("approve", "deny") }
    Card(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 4.dp)
            .semantics { contentDescription = "Approval needed" },
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.secondaryContainer,
        ),
    ) {
        Column(Modifier.padding(12.dp)) {
            Text(
                "Approval needed",
                style = MaterialTheme.typography.titleSmall,
                modifier = Modifier.semantics { heading() },
            )
            Spacer(Modifier.height(4.dp))
            Text(
                req.description?.ifBlank { null }
                    ?: req.command?.let { "Run: $it" }
                    ?: "The agent wants to run a tool.",
                style = MaterialTheme.typography.bodyMedium,
            )
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                choices.forEach { choice ->
                    TextButton(
                        onClick = { onChoice(choice) },
                        modifier = Modifier.semantics {
                            contentDescription = "Approval choice: $choice"
                        },
                    ) {
                        Text(choice.replaceFirstChar { it.uppercase() })
                    }
                }
            }
        }
    }
}


@Composable
private fun Composer(vm: ChatViewModel) {
    Column(Modifier.fillMaxWidth()) {
        if (vm.attachedImages.isNotEmpty()) {
            LazyRow(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 12.dp)
                    .semantics { contentDescription = "Attached images" },
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                items(vm.attachedImages, key = { it.source }) { image ->
                    Box {
                        Image(
                            bitmap = image.preview.asImageBitmap(),
                            contentDescription = "Shared image attachment",
                            modifier = Modifier.size(72.dp).clip(RoundedCornerShape(8.dp)),
                        )
                        IconButton(
                            onClick = { vm.removeAttachedImage(image) },
                            modifier = Modifier.size(28.dp).align(Alignment.TopEnd)
                                .semantics { contentDescription = "Remove image" },
                        ) {
                            Icon(Icons.Filled.Close, contentDescription = null)
                        }
                    }
                }
            }
        }
        Row(
            modifier = Modifier.fillMaxWidth().padding(12.dp),
            verticalAlignment = Alignment.Bottom,
        ) {
        OutlinedTextField(
            value = vm.input,
            onValueChange = { vm.input = it },
            label = { Text("Message") },
            modifier = Modifier.weight(1f)
                .semantics { contentDescription = "Message input" },
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
            keyboardActions = KeyboardActions(onSend = { vm.send() }),
            enabled = !vm.streaming,
            maxLines = 5,
        )
        Spacer(Modifier.size(8.dp))
        if (vm.streaming) {
            IconButton(
                onClick = { vm.stop() },
                modifier = Modifier.size(48.dp)
                    .semantics { contentDescription = "Stop generating" },
            ) {
                Icon(Icons.Filled.Stop, contentDescription = null)
            }
        } else {
            IconButton(
                onClick = { vm.send() },
                enabled = (vm.input.isNotBlank() || vm.attachedImages.isNotEmpty())
                    && vm.selectedSessionId != null,
                modifier = Modifier.size(48.dp)
                    .semantics { contentDescription = "Send message" },
            ) {
                Icon(Icons.AutoMirrored.Filled.Send, contentDescription = null)
            }
        }
    }
    }
}
