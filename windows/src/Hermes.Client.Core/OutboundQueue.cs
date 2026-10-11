namespace Hermes.Client.Core;

/// <summary>
/// One outbound chat message waiting for the gateway. The store owns
/// persistence: once EnqueueAsync returns, the message survives app restarts.
/// An attached photo rides along as raw bytes + MIME type (serialized as
/// base64 by the JSON store); the flusher sends it through the image chat
/// path so a queued photo is never silently dropped.
/// </summary>
public sealed record QueuedMessage(
    string Id,
    string SessionId,
    string Text,
    DateTime EnqueuedUtc,
    int AttemptCount,
    string? LastError,
    byte[]? ImageData = null,
    string? ImageMimeType = null)
{
    /// <summary>Rebuilds the attachment the flusher sends, or null when text-only.</summary>
    public ChatImageAttachment? ImageAttachment =>
        ImageData is { Length: > 0 } && !string.IsNullOrEmpty(ImageMimeType)
            ? new ChatImageAttachment(ImageData, ImageMimeType)
            : null;
}

/// <summary>
/// Durable FIFO store for outbound chat messages. Implementations must be
/// safe to call from any thread.
/// </summary>
public interface IOutboundMessageQueue
{
    Task<QueuedMessage> EnqueueAsync(string sessionId, string text, CancellationToken cancellationToken = default);

    Task<QueuedMessage> EnqueueAsync(
        string sessionId, string text, ChatImageAttachment? image, CancellationToken cancellationToken = default);

    /// <summary>Pending messages oldest-first (includes previously failed ones, for retry).</summary>
    Task<IReadOnlyList<QueuedMessage>> GetPendingAsync(CancellationToken cancellationToken = default);

    Task MarkSentAsync(string id, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(string id, string error, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

public sealed class QueuedMessageSentEventArgs(QueuedMessage message, string assistantText) : EventArgs
{
    public QueuedMessage Message { get; } = message;
    public string AssistantText { get; } = assistantText;
}

/// <summary>
/// Progress of one <see cref="OutboundQueueFlusher"/> drain: messages
/// completed (sent or failed) out of the total pending when the flush began.
/// </summary>
public sealed class FlushProgressEventArgs(int completed, int total) : EventArgs
{
    public int Completed { get; } = completed;
    public int Total { get; } = total;
}

/// <summary>
/// Drains the outbound queue FIFO through <c>POST /api/sessions/{id}/chat</c>
/// (IGatewayClient.SendChatAsync, non-streaming per the verified contract).
/// Sent messages are dequeued; gateway-side failures (4xx, malformed, …) are
/// marked failed and the flush continues with the next message; connectivity
/// failures (unreachable, timeout, HTTP 429) abort the flush so nothing is
/// burned while the gateway is down — the rest stays queued for the next
/// flush. Concurrent flushes serialize; each message is sent at most once.
/// </summary>
public sealed class OutboundQueueFlusher
{
    private readonly IGatewayClient _client;
    private readonly IOutboundMessageQueue _queue;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler<QueuedMessageSentEventArgs>? MessageSent;
    public event EventHandler<QueuedMessage>? MessageFailed;

    /// <summary>
    /// Raised as the flush drains: (0, total) at start, then (completed, total)
    /// after each message. Backs the taskbar progress for queue flushes.
    /// </summary>
    public event EventHandler<FlushProgressEventArgs>? Progress;

    public OutboundQueueFlusher(IGatewayClient client, IOutboundMessageQueue queue)
    {
        _client = client;
        _queue = queue;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = await _queue.GetPendingAsync(cancellationToken).ConfigureAwait(false);
            var completed = 0;
            Progress?.Invoke(this, new FlushProgressEventArgs(completed, pending.Count));
            foreach (var message in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var reply = message.ImageAttachment is { } image
                        ? await _client
                            .SendChatWithImageAsync(message.SessionId, message.Text, image, cancellationToken)
                            .ConfigureAwait(false)
                        : await _client
                            .SendChatAsync(message.SessionId, message.Text, cancellationToken)
                            .ConfigureAwait(false);
                    await _queue.MarkSentAsync(message.Id, cancellationToken).ConfigureAwait(false);
                    MessageSent?.Invoke(this, new QueuedMessageSentEventArgs(message, reply.Content));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsConnectivityFailure(ex))
                {
                    // Gateway down or rate-limited: stop, keep the rest queued.
                    break;
                }
                catch (Exception ex)
                {
                    await _queue.MarkFailedAsync(message.Id, ex.Message, cancellationToken).ConfigureAwait(false);
                    MessageFailed?.Invoke(this, message);
                }
                finally
                {
                    completed++;
                    Progress?.Invoke(this, new FlushProgressEventArgs(completed, pending.Count));
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Classifies a send failure: connectivity-class failures (unreachable,
    /// timeout/cancelled-by-timeout, HTTP 429) mean "try again later" and
    /// abort a flush; everything else is a message-level failure.
    /// </summary>
    public static bool IsConnectivityFailure(Exception ex) =>
        ex is HttpRequestException or TimeoutException or OperationCanceledException ||
        (ex is GatewayApiException api && api.StatusCode == 429);
}
