using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// The quick-entry box submits text by creating a session and then sending
/// one chat turn into it. These tests pin that order and the empty-text
/// guard; the WinUI window around the service is untestable here.
/// </summary>
public sealed class QuickEntryServiceTests
{
    [Fact]
    public async Task SubmitAsync_CreatesSessionThenSendsChat()
    {
        var fake = new RecordingGateway();
        var service = new QuickEntryService(fake);

        var session = await service.SubmitAsync("hello from anywhere");

        Assert.Equal("sess-1", session.Id);
        Assert.Equal(["create", "chat"], fake.Calls);
        Assert.Equal("sess-1", fake.ChatSessionId);
        Assert.Equal("hello from anywhere", fake.ChatContent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubmitAsync_BlankText_Throws(string? text)
    {
        var service = new QuickEntryService(new RecordingGateway());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.SubmitAsync(text!));
    }

    [Fact]
    public async Task SubmitAsync_CreateFails_ChatNeverSent()
    {
        var fake = new RecordingGateway { FailCreate = true };
        var service = new QuickEntryService(fake);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.SubmitAsync("x"));
        Assert.Equal(["create"], fake.Calls);
    }

    private sealed class RecordingGateway : IGatewayClient
    {
        public bool FailCreate { get; set; }
        public List<string> Calls { get; } = [];
        public string? ChatSessionId { get; private set; }
        public string? ChatContent { get; private set; }

        public Task<Session> CreateSessionAsync(string? title = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("create");
            if (FailCreate)
            {
                throw new HttpRequestException("No connection could be made");
            }

            return Task.FromResult(new Session("sess-1", "Quick entry", null, 0, null, null, null, null, false, false));
        }

        public Task<ChatMessage> SendChatAsync(string sessionId, string content, CancellationToken cancellationToken = default)
        {
            Calls.Add("chat");
            ChatSessionId = sessionId;
            ChatContent = content;
            return Task.FromResult(new ChatMessage("msg-1", sessionId, "assistant", content, DateTimeOffset.UtcNow, null, null, null, null, null));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<SessionPage> GetSessionsPageAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Session>> GetAllSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Session?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SseEvent> StreamChatEventsAsync(string sessionId, string message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SseEvent> StreamChatEventsWithImageAsync(string sessionId, string text, ChatImageAttachment image, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ChatMessage> SendChatWithImageAsync(string sessionId, string text, ChatImageAttachment image, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<string> StreamChatAsync(string sessionId, string content, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ApprovalResolution> ResolveApprovalAsync(string runId, string choice, string? requestId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ArtifactDownload> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ArtifactDownload> DownloadArtifactWithProgressAsync(string artifactId, IProgress<ArtifactDownloadProgress>? progress, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJobList> GetCronJobsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> GetCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> CreateCronJobAsync(CronJobCreateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> UpdateCronJobAsync(string jobId, CronJobUpdateRequest update, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> PauseCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> ResumeCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> RunCronJobAsync(string jobId, string? extraPrompt = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DetailedHealth> GetDetailedHealthAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunCreateResponse> CreateRunAsync(string input, string? sessionId = null, string? model = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunStatus> GetRunAsync(string runId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<RunEvent> StreamRunEventsAsync(string runId, long? lastSeq = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SteerResponse> SteerRunAsync(string runId, string message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunStatus> StopRunAsync(string runId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Session> ForkSessionAsync(string sessionId, string? title = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ToolsetInfo>> GetToolsetsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
