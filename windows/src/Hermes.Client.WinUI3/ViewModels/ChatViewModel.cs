using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Hermes.Client.WinUI3.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Chat transcript for one session: history via GET /api/sessions/{id}/messages,
/// turns via POST /api/sessions/{id}/chat/stream (typed SSE). Deltas append per
/// message_id; tool.progress with tool_name "_thinking" renders as reasoning;
/// approval.request surfaces an approval card; artifact download links in turn
/// output are fetched into the artifact pane.
///
/// Offline queue: when the gateway is unreachable the message is stored
/// durably (FileOutboundMessageQueue) instead of being lost. Queued items
/// show "Queued — will send on reconnect" in the transcript, the queue
/// indicator shows the pending count, and the queue flushes FIFO on
/// reconnect via POST /api/sessions/{id}/chat (non-streaming). Queue state
/// changes are announced to screen readers via AnnounceRequested.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IGatewayClient _client;
    private readonly UsageMeter _meter;
    private readonly IOutboundMessageQueue _queue;
    private readonly OutboundQueueFlusher _flusher;
    private readonly IConnectionState _connection;
    private readonly ICameraCaptureService _camera;
    private readonly TaskbarProgressTracker _tracker;
    private readonly ToolDiffRegistry _diffs;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Dictionary<string, AssistantMessageItem> _liveMessages = new();
    private readonly Dictionary<string, QueuedMessageItem> _queueItems = new();
    private readonly HashSet<string> _seenArtifacts = new();
    private CancellationTokenSource? _streamCts;
    private string? _sessionId;
    private int _flushRunning;

    // The gateway emits no artifact events; artifact references surface as
    // download URLs in turn text (verified against gateway source, 2026-10-11).
    private static readonly Regex ArtifactLink =
        new(@"/v1/artifacts/download/([A-Za-z0-9_-]+)", RegexOptions.Compiled);

    public ChatViewModel(
        IGatewayClient client,
        UsageMeter meter,
        IOutboundMessageQueue queue,
        OutboundQueueFlusher flusher,
        IConnectionState connection,
        ToolDiffRegistry diffs,
        ICameraCaptureService camera,
        TaskbarProgressTracker tracker)
    {
        _client = client;
        _meter = meter;
        _queue = queue;
        _flusher = flusher;
        _connection = connection;
        _diffs = diffs;
        _camera = camera;
        _tracker = tracker;
        _flusher.MessageSent += OnFlusherMessageSent;
        _flusher.MessageFailed += OnFlusherMessageFailed;
        _connection.StateChanged += OnConnectionStateChanged;
    }

    /// <summary>
    /// Raised for screen-reader announcements (queue state changes). The view
    /// relays these via AutomationPeer notification events.
    /// </summary>
    public event EventHandler<string>? AnnounceRequested;

    /// <summary>
    /// Unsubscribes from the singleton flusher/connection events. Called when
    /// the view navigates away (this VM is transient; the flusher is not).
    /// </summary>
    public void Detach()
    {
        _flusher.MessageSent -= OnFlusherMessageSent;
        _flusher.MessageFailed -= OnFlusherMessageFailed;
        _connection.StateChanged -= OnConnectionStateChanged;
    }

    public ObservableCollection<TranscriptItem> Transcript { get; } = new();
    public ObservableCollection<ArtifactItem> Artifacts { get; } = new();

    [ObservableProperty]
    public partial string? SessionTitle { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string InputText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsStreaming { get; set; }

    /// <summary>Photo captured from the camera, waiting to send with the next message.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(HasPendingPhoto))]
    public partial CapturedPhoto? PendingPhoto { get; set; }

    /// <summary>Preview bitmap for the pending photo (UI thread).</summary>
    [ObservableProperty]
    public partial BitmapImage? PendingPhotoImage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(CapturePhotoCommand))]
    public partial bool IsCapturingPhoto { get; set; }

    public bool HasPendingPhoto => PendingPhoto is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArtifactPaneVisible))]
    public partial bool HasArtifacts { get; set; }

    /// <summary>Durably queued outbound messages not yet sent to the gateway.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQueued))]
    [NotifyPropertyChangedFor(nameof(QueueStatusText))]
    public partial int QueuedCount { get; set; }

    public bool HasQueued => QueuedCount > 0;

    public string QueueStatusText => QueuedCount == 1
        ? "1 message queued — will send on reconnect."
        : $"{QueuedCount} messages queued — will send on reconnect.";

    public bool HasError => Error is not null;

    public bool ArtifactPaneVisible => HasArtifacts;

    private bool CanSend() =>
        !IsStreaming && !IsCapturingPhoto
        && (!string.IsNullOrWhiteSpace(InputText) || PendingPhoto is not null)
        && _sessionId is not null;

    private bool CanCapturePhoto() => !IsCapturingPhoto && !IsStreaming && _sessionId is not null;

    /// <summary>
    /// Captures one still frame from the device camera and stages it as the
    /// pending attachment for the next send. The camera is opened only for
    /// the capture and released immediately after.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCapturePhoto))]
    public async Task CapturePhotoAsync()
    {
        if (IsCapturingPhoto)
        {
            return;
        }

        IsCapturingPhoto = true;
        Error = null;
        try
        {
            var photo = await _camera.CapturePhotoAsync();
            PendingPhoto = photo;
            PendingPhotoImage = await LoadImageAsync(photo.JpegData);
            Announce("Photo attached. It will be sent with your next message.");
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            IsCapturingPhoto = false;
        }
    }

    [RelayCommand]
    public void RemovePhoto()
    {
        PendingPhoto = null;
        PendingPhotoImage = null;
        Announce("Photo removed.");
    }

    public async Task OpenSessionAsync(string sessionId, string? title)
    {
        CancelStreaming();
        _sessionId = sessionId;
        SessionTitle = string.IsNullOrWhiteSpace(title) ? sessionId : title;
        Transcript.Clear();
        Artifacts.Clear();
        HasArtifacts = false;
        Error = null;
        _liveMessages.Clear();
        _seenArtifacts.Clear();
        SendCommand.NotifyCanExecuteChanged();

        // Seed the usage meter from the session's stored totals; reset per session.
        _meter.BeginSession(sessionId, 0, 0, null);
        try
        {
            var session = await _client.GetSessionAsync(sessionId);
            if (session is not null)
            {
                _meter.BeginSession(sessionId, session.InputTokens, session.OutputTokens, session.EstimatedCostUsd);
            }
        }
        catch (Exception ex)
        {
            // Seeding is best-effort; the chat itself must work without it.
            System.Diagnostics.Debug.WriteLine($"Usage meter seed failed: {ex.Message}");
        }

        try
        {
            var messages = await _client.GetMessagesAsync(sessionId);
            foreach (var message in messages)
            {
                if (message.DisplayKind == "hidden")
                {
                    continue;
                }

                Transcript.Add(message.Role switch
                {
                    "user" => new UserMessageItem { Text = message.Content },
                    "assistant" => new AssistantMessageItem
                    {
                        Text = message.Content,
                        Reasoning = message.ReasoningContent ?? message.Reasoning ?? "",
                    },
                    "tool" => new ToolMessageItem
                    {
                        ToolName = message.ToolName,
                        Text = message.Content,
                    },
                    _ => new UserMessageItem { Text = message.Content },
                });
            }
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }

        // Re-surface anything still waiting in the durable queue (e.g. left
        // over from before an app restart) for this session.
        _queueItems.Clear();
        foreach (var pending in await _queue.GetPendingAsync())
        {
            if (pending.SessionId != sessionId)
            {
                continue;
            }

            var item = new QueuedMessageItem(pending, RetryQueuedAsync)
            {
                Status = pending.LastError is null ? "Queued — will send on reconnect." : "Send failed.",
                Error = pending.LastError,
            };
            _queueItems[pending.Id] = item;
            Transcript.Add(item);
        }

        RefreshQueuedCount();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendAsync()
    {
        var text = InputText.Trim();
        var sessionId = _sessionId;
        var photo = PendingPhoto;
        var photoPreview = PendingPhotoImage;
        if ((string.IsNullOrEmpty(text) && photo is null) || sessionId is null || IsStreaming)
        {
            return;
        }

        InputText = "";
        PendingPhoto = null;
        PendingPhotoImage = null;
        Error = null;

        // The gateway accepts image_url parts with data:image/... URLs
        // (verified against gateway source, 2026-10-11).
        ChatImageAttachment? image = photo is null
            ? null
            : new ChatImageAttachment(photo.JpegData, "image/jpeg");

        if (_connection.State == ConnectionState.Disconnected)
        {
            // Known offline: skip the doomed request, queue durably at once.
            // The photo rides in the queue entry — never silently dropped.
            await QueueMessageAsync(sessionId, text, image);
            return;
        }

        var userItem = new UserMessageItem { Text = text, Image = photoPreview };
        var assistant = new AssistantMessageItem();
        Transcript.Add(userItem);
        Transcript.Add(assistant);
        IsStreaming = true;
        _streamCts = new CancellationTokenSource();
        var runStarted = false;

        try
        {
            var events = image is null
                ? _client.StreamChatEventsAsync(sessionId, text, _streamCts.Token)
                : _client.StreamChatEventsWithImageAsync(sessionId, text, image, _streamCts.Token);
            await foreach (var e in events)
            {
                using (e)
                {
                    if (e.Name is "run.started" or "message.started")
                    {
                        runStarted = true;
                    }

                    HandleEvent(e, assistant);
                }
            }
        }
        catch (OperationCanceledException)
        {
            assistant.Status = "Cancelled.";
        }
        catch (Exception ex) when (!runStarted && OutboundQueueFlusher.IsConnectivityFailure(ex))
        {
            // Never reached the gateway (no run started server-side): queue
            // durably instead of losing the message. After run.started the
            // server has the message, so re-queueing would duplicate it.
            Transcript.Remove(userItem);
            Transcript.Remove(assistant);
            await QueueMessageAsync(sessionId, text, image);
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
            assistant.Status = "Failed.";
        }
        finally
        {
            IsStreaming = false;
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    public void CancelStreaming() => _streamCts?.Cancel();

    /// <summary>
    /// Re-anchors the meter to the gateway's authoritative session totals and
    /// estimated cost. Best-effort: per-turn token counts are already recorded
    /// from the SSE usage payload, so a failed refresh only leaves cost stale.
    /// </summary>
    private async Task RefreshMeterFromSessionAsync()
    {
        var sessionId = _sessionId;
        if (sessionId is null)
        {
            return;
        }

        try
        {
            var session = await _client.GetSessionAsync(sessionId).ConfigureAwait(false);
            if (session is not null)
            {
                _dispatcher.TryEnqueue(() => _meter.SyncFromSession(session));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Usage meter refresh failed: {ex.Message}");
        }
    }

    private void Announce(string message) =>
        AnnounceRequested?.Invoke(this, message);

    /// <summary>
    /// Stores the message durably and shows it in the transcript with the
    /// "Queued — will send on reconnect" state. An attached photo is stored
    /// with the entry and sent through the image chat path on flush.
    /// </summary>
    private async Task QueueMessageAsync(string sessionId, string text, ChatImageAttachment? image)
    {
        // The store requires non-blank text; a photo-only message queues with
        // an empty body and the photo attached.
        var queued = await _queue.EnqueueAsync(sessionId, text, image).ConfigureAwait(false);
        _dispatcher.TryEnqueue(() =>
        {
            var item = new QueuedMessageItem(queued, RetryQueuedAsync);
            _queueItems[queued.Id] = item;
            Transcript.Add(item);
            RefreshQueuedCount();
            Announce(image is null
                ? "Message queued. It will send when the connection is restored."
                : "Message with photo queued. It will send when the connection is restored.");
        });
    }

    private async Task RetryQueuedAsync(QueuedMessage message)
    {
        await FlushQueueAsync().ConfigureAwait(false);
        _dispatcher.TryEnqueue(() =>
        {
            // If the flush left this message untouched (gateway still down),
            // restore its queued state instead of hanging on "Sending…".
            if (_queueItems.TryGetValue(message.Id, out var item) && item.Status == "Sending…")
            {
                _ = RestoreItemStateAsync(item);
            }
        });
    }

    private async Task RestoreItemStateAsync(QueuedMessageItem item)
    {
        var current = (await _queue.GetPendingAsync().ConfigureAwait(false))
            .FirstOrDefault(m => m.Id == item.Message.Id);
        _dispatcher.TryEnqueue(() =>
        {
            if (current is null)
            {
                return;
            }

            item.Status = current.LastError is null ? "Queued — will send on reconnect." : "Send failed.";
            item.Error = current.LastError;
        });
    }

    /// <summary>
    /// Drains the queue in the background (flushes are serialized inside the
    /// flusher; this guard just avoids stacking redundant flush requests).
    /// The drain is tracked as a taskbar-progress operation: indeterminate
    /// while starting, determinate by message count as the flusher reports.
    /// </summary>
    private async Task FlushQueueAsync()
    {
        if (Interlocked.Exchange(ref _flushRunning, 1) == 1)
        {
            return;
        }

        var before = await _queue.CountAsync().ConfigureAwait(false);
        if (before == 0)
        {
            Interlocked.Exchange(ref _flushRunning, 0);
            return;
        }

        using var operation = _tracker.BeginOperation();
        try
        {
            void OnProgress(object? sender, FlushProgressEventArgs e) =>
                operation.Report(e.Total <= 0 ? 0 : (double)e.Completed / e.Total);
            _flusher.Progress += OnProgress;
            try
            {
                await _flusher.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _flusher.Progress -= OnProgress;
            }
        }
        catch (Exception ex)
        {
            _dispatcher.TryEnqueue(() => Error = FriendlyError(ex));
        }
        finally
        {
            Interlocked.Exchange(ref _flushRunning, 0);
        }

        var after = await _queue.CountAsync().ConfigureAwait(false);
        if (before > 0 && after == 0)
        {
            Announce("All queued messages sent.");
        }
    }

    private void RefreshQueuedCount() => _ = RefreshQueuedCountAsync();

    private async Task RefreshQueuedCountAsync()
    {
        var count = await _queue.CountAsync().ConfigureAwait(false);
        _dispatcher.TryEnqueue(() => QueuedCount = count);
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        if (state == ConnectionState.Connected)
        {
            _ = FlushQueueAsync();
        }
    }

    private void OnFlusherMessageSent(object? sender, QueuedMessageSentEventArgs e) =>
        _dispatcher.TryEnqueue(async () =>
        {
            if (_queueItems.Remove(e.Message.Id, out var item))
            {
                var index = Transcript.IndexOf(item);
                Transcript.Remove(item);
                if (e.Message.SessionId == _sessionId && index >= 0)
                {
                    Transcript.Insert(index, new UserMessageItem
                    {
                        Text = e.Message.Text,
                        Image = await LoadImageOrNullAsync(e.Message.ImageAttachment?.Data),
                    });
                    Transcript.Insert(index + 1, new AssistantMessageItem { Text = e.AssistantText });
                }
            }

            RefreshQueuedCount();
            Announce("Queued message sent.");
            // A flushed message also spent tokens server-side: re-anchor the meter.
            _ = RefreshMeterFromSessionAsync();
        });

    private void OnFlusherMessageFailed(object? sender, QueuedMessage e) =>
        _dispatcher.TryEnqueue(() => _ = ApplyFailedStateAsync(e));

    private async Task ApplyFailedStateAsync(QueuedMessage failed)
    {
        var current = (await _queue.GetPendingAsync().ConfigureAwait(false))
            .FirstOrDefault(m => m.Id == failed.Id);
        if (_queueItems.TryGetValue(failed.Id, out var item))
        {
            item.Status = "Send failed.";
            item.Error = current?.LastError ?? "The gateway rejected the message.";
            Announce($"Queued message failed to send: {item.Error}");
        }

        RefreshQueuedCount();
    }

    private AssistantMessageItem ItemFor(string? messageId, AssistantMessageItem current) =>
        messageId is not null && _liveMessages.TryGetValue(messageId, out var item) ? item : current;

    private void HandleEvent(SseEvent e, AssistantMessageItem current)
    {
        switch (e.Name)
        {
            case "run.started":
                current.Status = "Working…";
                break;

            case "message.started":
            {
                var id = e.GetString("message_id");
                if (id is not null)
                {
                    _liveMessages[id] = current;
                }

                break;
            }

            case "assistant.delta":
            {
                var item = ItemFor(e.GetString("message_id"), current);
                if (e.GetString("delta") is { } delta)
                {
                    item.Text += delta;
                }

                item.Status = "";
                break;
            }

            case "tool.progress":
            {
                var item = ItemFor(e.GetString("message_id"), current);
                var delta = e.GetString("delta") ?? "";
                if (e.GetString("tool_name") == "_thinking")
                {
                    item.Reasoning += delta;
                }
                else if (delta.Length > 0)
                {
                    item.Status = delta;
                }

                break;
            }

            case "tool.started":
                current.Status = $"Running {e.GetString("tool_name") ?? "tool"}…";
                break;

            case "tool.completed":
                current.Status = $"{e.GetString("tool_name") ?? "Tool"} finished.";
                ScanArtifacts(e.GetString("preview") ?? "");
                RegisterToolDiff(e);
                break;

            case "tool.failed":
                current.Status = $"{e.GetString("tool_name") ?? "Tool"} failed.";
                break;

            case "assistant.commentary":
                if (e.GetString("text") is { } commentary)
                {
                    current.Commentary += commentary;
                }

                break;

            case "approval.request":
                current.Status = "";
                Transcript.Add(new ApprovalItem(_client, ApprovalRequest.FromEvent(e)));
                break;

            case "assistant.completed":
            {
                var item = ItemFor(e.GetString("message_id"), current);
                if (e.GetString("content") is { } content)
                {
                    item.Text = content;
                }

                item.Status = "";
                ScanArtifacts(item.Text);
                break;
            }

            case "run.completed":
                current.Status = "";
                if (TurnUsage.FromEvent(e, _meter.Turns.Count + 1) is { } turn)
                {
                    _meter.RecordTurn(turn);
                    _ = RefreshMeterFromSessionAsync();
                }

                break;

            case "run.cancelled":
                current.Status = "Cancelled.";
                break;

            case "run.failed":
                current.Status = "Run failed.";
                Error = e.GetString("message") ?? "The run failed.";
                break;

            case "error":
                Error = e.GetString("message") ?? "Stream error.";
                current.Status = "Failed.";
                break;

            case "done":
                current.Status = "";
                break;
        }
    }

    /// <summary>
    /// Feeds a completed tool call's <c>inline_diff</c> into the shared
    /// <see cref="ToolDiffRegistry"/> and appends a <see cref="DiffItem"/> so
    /// the transcript offers per-hunk review. No-op when the gateway sent no
    /// diff (the 8642 HTTP streams don't forward <c>inline_diff</c> today —
    /// verified 2026-10-11).
    /// </summary>
    private void RegisterToolDiff(SseEvent e)
    {
        var key = ToolDiffExtractor.GetToolCallId(e) ?? e.GetString("message_id");
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var files = ToolDiffExtractor.ExtractFiles(e);
        if (files.Count == 0)
        {
            return;
        }

        _diffs.Set(key, files);
        Transcript.Add(new DiffItem
        {
            ToolCallId = key,
            ToolName = e.GetString("tool_name"),
            FileCount = files.Count,
            DiffSource = _diffs,
        });
    }

    private void ScanArtifacts(string text)
    {
        foreach (Match match in ArtifactLink.Matches(text))
        {
            var id = match.Groups[1].Value;
            if (_seenArtifacts.Add(id))
            {
                _ = DownloadArtifactAsync(id);
            }
        }
    }

    private async Task DownloadArtifactAsync(string artifactId)
    {
        // Taskbar progress: indeterminate until the gateway's Content-Length
        // is known, then determinate with real byte counts.
        using var operation = _tracker.BeginOperation();
        try
        {
            var progress = new Progress<ArtifactDownloadProgress>(p =>
            {
                if (p.TotalBytes is null or <= 0)
                {
                    operation.ReportIndeterminate();
                }
                else
                {
                    operation.Report((double)p.BytesReceived / p.TotalBytes.Value);
                }
            });
            var download = await _client.DownloadArtifactWithProgressAsync(artifactId, progress);
            var item = new ArtifactItem
            {
                ArtifactId = download.ArtifactId,
                FileName = download.FileName,
                ContentType = download.ContentType,
                Sha256 = download.Sha256,
                Data = download.Data,
            };
            if (item.IsImage && download.Data is not null)
            {
                item.Image = await LoadImageAsync(download.Data);
            }

            Artifacts.Add(item);
            HasArtifacts = true;
        }
        catch (GatewayApiException ex) when (ex.StatusCode is 404 or 410)
        {
            // One-shot download already consumed, or expired: nothing to show.
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
    }

    private static async Task<BitmapImage> LoadImageAsync(byte[] data)
    {
        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(data.AsBuffer());
        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    /// <summary>Decodes attached photo bytes for the transcript; null when absent or undecodable.</summary>
    private static async Task<BitmapImage?> LoadImageOrNullAsync(byte[]? data)
    {
        if (data is null)
        {
            return null;
        }

        try
        {
            return await LoadImageAsync(data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        GatewayApiException { StatusCode: 401 } =>
            "Invalid gateway API key. Check the connection settings.",
        GatewayApiException api => $"Gateway error {api.StatusCode}: {api.Message}",
        HttpRequestException => "Cannot reach the gateway. Check the connection settings.",
        _ => ex.Message,
    };
}
