namespace Hermes.Client.Core;

/// <summary>
/// One outbound chat message waiting for the gateway. The store owns
/// persistence: once EnqueueAsync returns, the message survives app restarts.
/// </summary>
public sealed record QueuedMessage(
    string Id,
    string SessionId,
    string Text,
    DateTime EnqueuedUtc,
    int AttemptCount,
    string? LastError);

/// <summary>
/// Durable FIFO store for outbound chat messages. Implementations must be
/// safe to call from any thread.
/// </summary>
public interface IOutboundMessageQueue
{
    Task<QueuedMessage> EnqueueAsync(string sessionId, string text, CancellationToken cancellationToken = default);

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
            foreach (var message in await _queue.GetPendingAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var reply = await _client
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
