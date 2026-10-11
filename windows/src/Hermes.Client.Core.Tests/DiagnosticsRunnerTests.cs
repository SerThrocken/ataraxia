using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Tests for <see cref="DiagnosticsRunner"/> — the native one-tap
/// diagnostics. <c>POST /api/ops/doctor</c> is serve-backend-only on the
/// Acer (registered in <c>hermes_cli/web_routers/ops.py</c>, not exposed
/// on the 8642 API surface), so the client runs the equivalent checks
/// against the verified API surface: <c>/health</c>, <c>/health/detailed</c>,
/// <c>/v1/models</c>, and the session store.
/// </summary>
public sealed class DiagnosticsRunnerTests
{
    private sealed class FakeClient : IGatewayClient
    {
        public bool Healthy = true;
        public DetailedHealth? Detailed;
        public Exception? DetailedError;
        public IReadOnlyList<ModelInfo> Models = [new ModelInfo("m1", "m1", null)];
        public SessionPage SessionPage = new([], 1, 0, false);

        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Healthy);

        public Task<DetailedHealth> GetDetailedHealthAsync(CancellationToken cancellationToken = default) =>
            DetailedError is not null
                ? Task.FromException<DetailedHealth>(DetailedError)
                : Task.FromResult(Detailed ?? throw new InvalidOperationException("no health stubbed"));

        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Models);

        public Task<SessionPage> GetSessionsPageAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(SessionPage);

        public Task<Session?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Session>> GetAllSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJobList> GetCronJobsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> GetCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> CreateCronJobAsync(CronJobCreateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> UpdateCronJobAsync(string jobId, CronJobUpdateRequest update, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> PauseCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> ResumeCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> RunCronJobAsync(string jobId, string? extraPrompt = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Session> CreateSessionAsync(string? title = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ChatMessage> SendChatAsync(string sessionId, string content, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SseEvent> StreamChatEventsAsync(string sessionId, string message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SseEvent> StreamChatEventsWithImageAsync(string sessionId, string text, ChatImageAttachment image, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ChatMessage> SendChatWithImageAsync(string sessionId, string text, ChatImageAttachment image, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<string> StreamChatAsync(string sessionId, string content, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ApprovalResolution> ResolveApprovalAsync(string runId, string choice, string? requestId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ArtifactDownload> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ArtifactDownload> DownloadArtifactWithProgressAsync(string artifactId, IProgress<ArtifactDownloadProgress>? progress, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ToolsetInfo>> GetToolsetsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunCreateResponse> CreateRunAsync(string input, string? sessionId = null, string? model = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunStatus> GetRunAsync(string runId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<RunEvent> StreamRunEventsAsync(string runId, long? lastSeq = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SteerResponse> SteerRunAsync(string runId, string message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunStatus> StopRunAsync(string runId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Session> ForkSessionAsync(string sessionId, string? title = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static DetailedHealth HealthyDetailed(Dictionary<string, PlatformStatus>? platforms = null) =>
        new(
            "ok",
            new ReadinessChecks("ok", new CheckSet(
                new CheckStatus("ok"), new CheckStatus("ok"), new CheckStatus("ok"), new CheckStatus("ok"),
                new DiskCheck("ok", 25.1, 1497278910464L),
                new GatewayCheck("ok", 13, 13, "running"),
                new BackgroundQueuesCheck("ok", 0, 0, 0))),
            "hermes-agent", "0.21.6", "running")
        {
            Platforms = platforms ?? new Dictionary<string, PlatformStatus>
            {
                ["a2a"] = new("connected", null, null, DateTimeOffset.UtcNow, 31620, 179173149276L, false, null, null, null),
            },
        };

    [Fact]
    public async Task AllGreenGatewayPassesEveryCheck()
    {
        var runner = new DiagnosticsRunner(new FakeClient { Detailed = HealthyDetailed() });

        var report = await runner.RunAsync();

        Assert.Equal(6, report.Checks.Count);
        Assert.All(report.Checks, c => Assert.Equal(DiagnosticStatus.Pass, c.Status));
        Assert.True(report.AllPassed);
    }

    [Fact]
    public async Task UnreachableGatewayFailsGracefully()
    {
        var runner = new DiagnosticsRunner(new FakeClient
        {
            Healthy = false,
            DetailedError = new HttpRequestException("no route to host"),
        });

        var report = await runner.RunAsync();

        var reachability = report.Checks[0];
        Assert.Equal(DiagnosticStatus.Fail, reachability.Status);
        Assert.False(report.AllPassed);
    }

    [Fact]
    public async Task FullDiskWarnsInsteadOfFailing()
    {
        var detailed = HealthyDetailed() with
        {
            Readiness = new ReadinessChecks("ok", new CheckSet(
                new CheckStatus("ok"), new CheckStatus("ok"), new CheckStatus("ok"), new CheckStatus("ok"),
                new DiskCheck("ok", 96.5, 1000000L),
                new GatewayCheck("ok", 13, 13, "running"),
                new BackgroundQueuesCheck("ok", 0, 0, 0))),
        };
        var runner = new DiagnosticsRunner(new FakeClient { Detailed = detailed });

        var report = await runner.RunAsync();

        var disk = report.Checks.First(c => c.Name.Contains("Disk", StringComparison.Ordinal));
        Assert.Equal(DiagnosticStatus.Warn, disk.Status);
        Assert.Contains("96.5", disk.Detail, StringComparison.Ordinal);
        Assert.False(report.AllPassed); // warnings count against a clean bill
    }

    [Fact]
    public async Task PlatformNeedingAttentionFailsThatCheck()
    {
        var platforms = new Dictionary<string, PlatformStatus>
        {
            ["a2a"] = new("degraded", "timeout", "heartbeat missed", DateTimeOffset.UtcNow, 31620, 179173149276L, true, null, null, null),
        };
        var runner = new DiagnosticsRunner(new FakeClient { Detailed = HealthyDetailed(platforms) });

        var report = await runner.RunAsync();

        var check = report.Checks.First(c => c.Name.Contains("Platform", StringComparison.Ordinal));
        Assert.Equal(DiagnosticStatus.Fail, check.Status);
        Assert.Contains("a2a", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyModelCatalogWarns()
    {
        var runner = new DiagnosticsRunner(new FakeClient
        {
            Detailed = HealthyDetailed(),
            Models = [],
        });

        var report = await runner.RunAsync();

        var models = report.Checks.First(c => c.Name.Contains("Model", StringComparison.Ordinal));
        Assert.Equal(DiagnosticStatus.Warn, models.Status);
    }
}
