using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

// TDD for the command palette engine: item building across skills / sessions /
// pages / actions, query filtering + ranking, effect mapping, and graceful
// degradation when the gateway misbehaves (notably GET /v1/skills → 500).
public sealed class CommandPaletteEngineTests
{
    private static readonly SkillInfo WeatherSkill = new("weather", "weather", "Current weather lookup");
    private static readonly Session BugSession =
        new("sess_1", "Fix restart bugs", null, 12, "openai/gpt-5", null, null, null, false, false);

    private static IReadOnlyList<PaletteItem> BuildSample() =>
        new CommandPaletteEngine().BuildItems([WeatherSkill], [BugSession]);

    [Fact]
    public void BuildItems_EmptySources_StillHasPagesAndActions()
    {
        var items = new CommandPaletteEngine().BuildItems([], []);
        Assert.Contains(items, i => i.Kind == PaletteItemKind.Action && i.Payload == CommandPaletteEngine.ActionNewSession);
        Assert.Contains(items, i => i.Kind == PaletteItemKind.Action && i.Payload == CommandPaletteEngine.ActionReconnect);
        Assert.Contains(items, i => i.Kind == PaletteItemKind.Page && i.Payload == "settings");
        Assert.Contains(items, i => i.Kind == PaletteItemKind.Page && i.Payload == "chat");
    }

    [Fact]
    public void BuildItems_IncludesSkillsAndSessions()
    {
        var items = BuildSample();
        var skill = Assert.Single(items, i => i.Kind == PaletteItemKind.Skill);
        Assert.Equal("weather", skill.Title);
        Assert.Equal("weather", skill.Payload);
        var session = Assert.Single(items, i => i.Kind == PaletteItemKind.Session);
        Assert.Equal("Fix restart bugs", session.Title);
        Assert.Equal("sess_1", session.Payload);
    }

    [Fact]
    public void Query_Empty_ReturnsEverything_InBuildOrder()
    {
        var items = BuildSample();
        var result = new CommandPaletteEngine().Query(items, "");
        Assert.Equal(items.Count, result.Count);
        Assert.Equal(items[0].Title, result[0].Title);
    }

    [Fact]
    public void Query_NoMatch_ReturnsEmpty()
    {
        var result = new CommandPaletteEngine().Query(BuildSample(), "zzz-no-such-thing");
        Assert.Empty(result);
    }

    [Fact]
    public void Query_MatchesSkill_ByNameSubstring()
    {
        var result = new CommandPaletteEngine().Query(BuildSample(), "weath");
        var skill = Assert.Single(result);
        Assert.Equal(PaletteItemKind.Skill, skill.Kind);
    }

    [Fact]
    public void Query_MatchesSession_ByTitleSubstring()
    {
        var result = new CommandPaletteEngine().Query(BuildSample(), "restart");
        var session = Assert.Single(result);
        Assert.Equal(PaletteItemKind.Session, session.Kind);
    }

    [Fact]
    public void Query_MatchesSubtitle_ForActions()
    {
        // "Start a new chat session" is the New-session subtitle.
        var result = new CommandPaletteEngine().Query(BuildSample(), "chat session");
        Assert.Contains(result, i => i.Payload == CommandPaletteEngine.ActionNewSession);
    }

    [Fact]
    public void Query_RanksPrefixMatch_First()
    {
        var result = new CommandPaletteEngine().Query(BuildSample(), "wea");
        Assert.Equal(PaletteItemKind.Skill, result[0].Kind);
    }

    [Fact]
    public void EffectFor_Skill_ReturnsInsertText()
    {
        var effect = new CommandPaletteEngine().EffectFor(
            new PaletteItem(PaletteItemKind.Skill, "weather", null, "weather"));
        Assert.Equal(PaletteEffectKind.InsertText, effect.Kind);
        Assert.Equal("/weather ", effect.Payload);
    }

    [Fact]
    public void EffectFor_Session_ReturnsOpenSession_WithTitle()
    {
        var effect = new CommandPaletteEngine().EffectFor(
            new PaletteItem(PaletteItemKind.Session, "Fix restart bugs", null, "sess_1"));
        Assert.Equal(PaletteEffectKind.OpenSession, effect.Kind);
        Assert.Equal("sess_1", effect.Payload);
        Assert.Equal("Fix restart bugs", effect.Label);
    }

    [Fact]
    public void EffectFor_Page_ReturnsNavigatePage()
    {
        var effect = new CommandPaletteEngine().EffectFor(
            new PaletteItem(PaletteItemKind.Page, "Open Settings", null, "settings"));
        Assert.Equal(PaletteEffectKind.NavigatePage, effect.Kind);
        Assert.Equal("settings", effect.Payload);
    }

    [Fact]
    public void EffectFor_Actions_MapToNewSessionAndReconnect()
    {
        var engine = new CommandPaletteEngine();
        Assert.Equal(PaletteEffectKind.NewSession,
            engine.EffectFor(new PaletteItem(PaletteItemKind.Action, "New session", null, CommandPaletteEngine.ActionNewSession)).Kind);
        Assert.Equal(PaletteEffectKind.Reconnect,
            engine.EffectFor(new PaletteItem(PaletteItemKind.Action, "Reconnect", null, CommandPaletteEngine.ActionReconnect)).Kind);
    }

    [Fact]
    public async Task GetSkillsSafeAsync_ReturnsEmpty_WhenGateway500s()
    {
        var fake = new PaletteFakeGateway { FailSkills = true };
        var skills = await CommandPaletteEngine.GetSkillsSafeAsync(fake);
        Assert.Empty(skills);
    }

    [Fact]
    public async Task GetSkillsSafeAsync_ReturnsSkills_WhenGatewayOk()
    {
        var skills = await CommandPaletteEngine.GetSkillsSafeAsync(new PaletteFakeGateway());
        Assert.Single(skills);
    }

    [Fact]
    public async Task GetSessionsSafeAsync_ReturnsEmpty_WhenGatewayUnreachable()
    {
        var fake = new PaletteFakeGateway { FailSessions = true };
        var sessions = await CommandPaletteEngine.GetSessionsSafeAsync(fake);
        Assert.Empty(sessions);
    }

    private sealed class PaletteFakeGateway : IGatewayClient
    {
        public bool FailSkills { get; set; }
        public bool FailSessions { get; set; }

        public Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default) =>
            FailSkills
                ? throw new GatewayApiException(500, "Failed to enumerate skills", "server_error", null)
                : Task.FromResult<IReadOnlyList<SkillInfo>>([WeatherSkill]);

        public Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            FailSessions
                ? throw new HttpRequestException("No connection could be made")
                : Task.FromResult<IReadOnlyList<Session>>([BugSession]);
        public Task<IReadOnlyList<Session>> GetAllSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<Session?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<SessionPage> GetSessionsPageAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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
        public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ToolsetInfo>> GetToolsetsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DetailedHealth> GetDetailedHealthAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunCreateResponse> CreateRunAsync(string input, string? sessionId = null, string? model = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunStatus> GetRunAsync(string runId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<RunEvent> StreamRunEventsAsync(string runId, long? lastSeq = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SteerResponse> SteerRunAsync(string runId, string message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RunStatus> StopRunAsync(string runId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Session> ForkSessionAsync(string sessionId, string? title = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJobList> GetCronJobsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> GetCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> CreateCronJobAsync(CronJobCreateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> UpdateCronJobAsync(string jobId, CronJobUpdateRequest update, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> PauseCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> ResumeCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CronJob> RunCronJobAsync(string jobId, string? extraPrompt = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
