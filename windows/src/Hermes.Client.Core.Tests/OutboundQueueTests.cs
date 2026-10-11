using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// TDD for the offline outbound-message queue. The flusher must send queued
/// messages FIFO through SendChatAsync, dequeue on success, mark failures
/// with attempt count + error (continuing with the next), and abort on
/// connectivity-class failures leaving the rest queued.
/// </summary>
public sealed class OutboundQueueTests
{
    private sealed class MemoryQueue : IOutboundMessageQueue
    {
        private readonly List<QueuedMessage> _items = new();

        public Task<QueuedMessage> EnqueueAsync(string sessionId, string text, CancellationToken cancellationToken = default)
        {
            var item = new QueuedMessage(
                Id: Guid.NewGuid().ToString("N"),
                SessionId: sessionId,
                Text: text,
                EnqueuedUtc: DateTime.UtcNow,
                AttemptCount: 0,
                LastError: null);
            lock (_items)
            {
                _items.Add(item);
            }

            return Task.FromResult(item);
        }

        public Task<IReadOnlyList<QueuedMessage>> GetPendingAsync(CancellationToken cancellationToken = default)
        {
            lock (_items)
            {
                return Task.FromResult<IReadOnlyList<QueuedMessage>>(
                    _items.OrderBy(m => m.EnqueuedUtc).ToList());
            }
        }

        public Task MarkSentAsync(string id, CancellationToken cancellationToken = default)
        {
            lock (_items)
            {
                _items.RemoveAll(m => m.Id == id);
            }

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(string id, string error, CancellationToken cancellationToken = default)
        {
            lock (_items)
            {
                var index = _items.FindIndex(m => m.Id == id);
                if (index >= 0)
                {
                    var m = _items[index];
                    _items[index] = m with { AttemptCount = m.AttemptCount + 1, LastError = error };
                }
            }

            return Task.CompletedTask;
        }

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            lock (_items)
            {
                return Task.FromResult(_items.Count);
            }
        }
    }

    private sealed class FakeClient : IGatewayClient
    {
        public readonly List<(string SessionId, string Content)> Sent = new();
        public Func<string, string, Task<ChatMessage>>? Behavior;

        public Task<ChatMessage> SendChatAsync(string sessionId, string content, CancellationToken cancellationToken = default)
        {
            lock (Sent)
            {
                Sent.Add((sessionId, content));
            }

            return Behavior is not null
                ? Behavior(sessionId, content)
                : Task.FromResult(new ChatMessage(null, sessionId, "assistant", "ok", null, null, null, null, null, null));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<SessionPage> GetSessionsPageAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<Session>> GetAllSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Session> CreateSessionAsync(string? title = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Session?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public IAsyncEnumerable<SseEvent> StreamChatEventsAsync(string sessionId, string message, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public IAsyncEnumerable<string> StreamChatAsync(string sessionId, string content, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<ApprovalResolution> ResolveApprovalAsync(string runId, string choice, string? requestId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<ArtifactDownload> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<CronJobList> GetCronJobsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<CronJob> GetCronJobAsync(string jobId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<CronJob> CreateCronJobAsync(CronJobCreateRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<CronJob> UpdateCronJobAsync(string jobId, CronJobUpdateRequest update, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> DeleteCronJobAsync(string jobId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<CronJob> PauseCronJobAsync(string jobId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<CronJob> ResumeCronJobAsync(string jobId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<CronJob> RunCronJobAsync(string jobId, string? extraPrompt = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<ToolsetInfo>> GetToolsetsAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<DetailedHealth> GetDetailedHealthAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RunCreateResponse> CreateRunAsync(string input, string? sessionId = null, string? model = null, string? idempotencyKey = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RunStatus> GetRunAsync(string runId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public IAsyncEnumerable<RunEvent> StreamRunEventsAsync(string runId, long? lastSeq = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<SteerResponse> SteerRunAsync(string runId, string message, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<RunStatus> StopRunAsync(string runId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Session> ForkSessionAsync(string sessionId, string? title = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private static async Task<(MemoryQueue Queue, FakeClient Client, OutboundQueueFlusher Flusher)> SetUpAsync()
    {
        var queue = new MemoryQueue();
        var client = new FakeClient();
        var flusher = new OutboundQueueFlusher(client, queue);
        await Task.CompletedTask;
        return (queue, client, flusher);
    }

    [Fact]
    public async Task FlushSendsQueuedMessagesInFifoOrder()
    {
        var (queue, client, flusher) = await SetUpAsync();
        await queue.EnqueueAsync("s1", "first");
        await queue.EnqueueAsync("s1", "second");
        await queue.EnqueueAsync("s2", "third");

        var sentTexts = new List<string>();
        flusher.MessageSent += (_, e) => sentTexts.Add(e.AssistantText);
        client.Behavior = (sessionId, content) =>
            Task.FromResult(new ChatMessage(null, sessionId, "assistant", $"reply:{content}", null, null, null, null, null, null));

        await flusher.FlushAsync();

        Assert.Equal(new[] { "first", "second", "third" }, client.Sent.Select(s => s.Content));
        Assert.Equal(new[] { "reply:first", "reply:second", "reply:third" }, sentTexts);
        Assert.Equal(0, await queue.CountAsync());
    }

    [Fact]
    public async Task FailedSendIsMarkedFailedAndFlushContinuesWithNext()
    {
        var (queue, client, flusher) = await SetUpAsync();
        var bad = await queue.EnqueueAsync("s1", "bad");
        await queue.EnqueueAsync("s1", "good");

        var failed = new List<QueuedMessage>();
        flusher.MessageFailed += (_, m) => failed.Add(m);
        client.Behavior = (sessionId, content) => content == "bad"
            ? Task.FromException<ChatMessage>(new GatewayApiException(400, "Bad request"))
            : Task.FromResult(new ChatMessage(null, sessionId, "assistant", "good-reply", null, null, null, null, null, null));

        await flusher.FlushAsync();

        var pending = await queue.GetPendingAsync();
        var failedItem = Assert.Single(pending);
        Assert.Equal(bad.Id, failedItem.Id);
        Assert.Equal(1, failedItem.AttemptCount);
        Assert.NotNull(failedItem.LastError);
        Assert.Single(failed);
    }

    [Fact]
    public async Task ConnectivityFailureAbortsFlushLeavingRestQueued()
    {
        var (queue, client, flusher) = await SetUpAsync();
        await queue.EnqueueAsync("s1", "one");
        await queue.EnqueueAsync("s1", "two");

        client.Behavior = (_, _) =>
            Task.FromException<ChatMessage>(new HttpRequestException("connection refused"));

        await flusher.FlushAsync();

        Assert.Single(client.Sent); // aborted after the first failure
        Assert.Equal(2, await queue.CountAsync()); // nothing dequeued, nothing marked failed
        var pending = await queue.GetPendingAsync();
        Assert.All(pending, m => Assert.Equal(0, m.AttemptCount));
    }

    [Fact]
    public async Task RateLimitedSendAbortsFlushWithoutMarkingFailed()
    {
        var (queue, client, flusher) = await SetUpAsync();
        await queue.EnqueueAsync("s1", "one");

        client.Behavior = (_, _) =>
            Task.FromException<ChatMessage>(new GatewayApiException(429, "Too many concurrent runs"));

        await flusher.FlushAsync();

        var pending = await queue.GetPendingAsync();
        var item = Assert.Single(pending);
        Assert.Equal(0, item.AttemptCount);
        Assert.Null(item.LastError);
    }

    [Fact]
    public async Task FailedMessageIsRetriedOnNextFlush()
    {
        var (queue, client, flusher) = await SetUpAsync();
        await queue.EnqueueAsync("s1", "retry-me");

        client.Behavior = (_, _) =>
            Task.FromException<ChatMessage>(new GatewayApiException(400, "Bad request"));
        await flusher.FlushAsync();
        Assert.Single(await queue.GetPendingAsync());

        client.Behavior = (sessionId, content) =>
            Task.FromResult(new ChatMessage(null, sessionId, "assistant", "finally", null, null, null, null, null, null));
        await flusher.FlushAsync();

        Assert.Equal(0, await queue.CountAsync());
        Assert.Equal(2, client.Sent.Count);
    }

    [Fact]
    public async Task ConcurrentFlushesNeverSendTheSameMessageTwice()
    {
        var (queue, client, flusher) = await SetUpAsync();
        await queue.EnqueueAsync("s1", "only-once");

        var concurrent = 0;
        var maxConcurrent = 0;
        var gate = new object();
        client.Behavior = async (sessionId, content) =>
        {
            lock (gate)
            {
                concurrent++;
                maxConcurrent = Math.Max(maxConcurrent, concurrent);
            }

            await Task.Delay(50);
            lock (gate)
            {
                concurrent--;
            }

            return new ChatMessage(null, sessionId, "assistant", "ok", null, null, null, null, null, null);
        };

        await Task.WhenAll(flusher.FlushAsync(), flusher.FlushAsync());

        Assert.Single(client.Sent);
        Assert.Equal(1, maxConcurrent);
        Assert.Equal(0, await queue.CountAsync());
    }

    [Fact]
    public async Task EmptyQueueFlushIsANoop()
    {
        var (_, client, flusher) = await SetUpAsync();

        await flusher.FlushAsync();

        Assert.Empty(client.Sent);
    }
}
