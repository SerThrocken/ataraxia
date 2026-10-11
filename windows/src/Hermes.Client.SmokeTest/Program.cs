// Connection-flow smoke test. Runs ON the Acer (the gateway host).
// The API key is read from the gateway's own .env into process memory only —
// it is never printed, logged, or written anywhere by this program.
using Hermes.Client.Core;
using Hermes.Client.Infrastructure;

var failures = new List<string>();
void Check(bool ok, string name)
{
    Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + name);
    if (!ok)
    {
        failures.Add(name);
    }
}
static bool VersionAtLeast(string? version, int major, int minor, int patch)
{
    if (!Version.TryParse(version?.TrimStart('v'), out var v)) return false;
    return v >= new Version(major, minor, patch);
}

if (args.Contains("--camera-probe"))
{
    // Camera-to-agent verification: capture one still frame from the device
    // camera and prove it is a real JPEG that survives the ChatImageAttachment
    // the send path uses. Runs headless on the Acer; the camera is released
    // as soon as the capture completes.
    var camera = new Hermes.Client.WinUI3.Services.MediaCaptureCameraService();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    try
    {
        var photo = await camera.CapturePhotoAsync(cts.Token);
        Check(photo.JpegData.Length > 1024, $"captured {photo.JpegData.Length} bytes from camera");
        Check(photo.JpegData[0] == 0xFF && photo.JpegData[1] == 0xD8, "JPEG magic bytes (FFD8)");
        Check(photo.JpegData[^2] == 0xFF && photo.JpegData[^1] == 0xD9, "JPEG end marker (FFD9)");
        var attachment = new ChatImageAttachment(photo.JpegData, "image/jpeg");
        Check(attachment.ToDataUrl().StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal),
            "data URL shape for the gateway image_url part");
        var path = Path.Combine(Path.GetTempPath(), "ataraxia-camera-probe.jpg");
        await File.WriteAllBytesAsync(path, photo.JpegData);
        Console.WriteLine($"probe frame saved to {path}");
    }
    catch (Exception ex)
    {
        Check(false, $"camera capture threw: {ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine(failures.Count == 0 ? "CAMERA PROBE OK" : $"CAMERA PROBE FAILED ({failures.Count})");
    return failures.Count == 0 ? 0 : 1;
}

var apiKey = ReadApiServerKey();
Check(!string.IsNullOrEmpty(apiKey), "API_SERVER_KEY present in gateway .env (memory only, never printed)");
Check(ConnectionValidation.IsValidApiKey(apiKey), "stored key passes client-side validation (>=16 chars)");
Check(!ConnectionValidation.IsValidApiKey("too-short"), "short key rejected client-side");
Check(!ConnectionValidation.IsValidApiKey(null), "missing key rejected client-side");
Check(ConnectionValidation.IsValidBaseUrl("http://100.101.250.118:8642"), "tailscale URL valid");
Check(ConnectionValidation.IsValidBaseUrl("http://127.0.0.1:8642"), "loopback URL valid");
Check(!ConnectionValidation.IsValidBaseUrl("not a url"), "bad URL rejected");
Check(!ConnectionValidation.IsValidBaseUrl("ftp://example.com/x"), "non-http URL rejected");

var tester = new ConnectionTester();

var live = await tester.TestAsync(new Uri("http://100.101.250.118:8642"), apiKey);
Console.WriteLine($"live: {live.Outcome} — {live.Message}");
Check(live.Succeeded, "live connectivity test succeeds");
Check(VersionAtLeast(live.Health?.Version, 0, 21, 6), $"gateway version >= 0.21.6 (got '{live.Health?.Version}')");
Check(live.Capabilities?.EnabledFeatures.Count > 0, "capabilities parsed (enabled features > 0)");
Check(live.Capabilities?.EndpointCount > 0, "capabilities parsed (endpoint count > 0)");
Console.WriteLine("enabled features: " + string.Join(", ", live.Capabilities?.EnabledFeatures.Take(8) ?? []));

var loopback = await tester.TestAsync(new Uri("http://127.0.0.1:8642"), apiKey);
Check(loopback.Succeeded && VersionAtLeast(loopback.Health?.Version, 0, 21, 6), "loopback fallback also reaches the gateway");

var badKey = await tester.TestAsync(new Uri("http://100.101.250.118:8642"), "wrong-key-0000000000");
Check(badKey.Outcome == ConnectionTestOutcome.Unauthorized,
    $"wrong key classifies as Unauthorized (got {badKey.Outcome}) — {badKey.Message}");

var dead = await tester.TestAsync(new Uri("http://127.0.0.1:8649"), apiKey);
Check(dead.Outcome == ConnectionTestOutcome.Unreachable,
    $"dead port classifies as Unreachable (got {dead.Outcome}) — {dead.Message}");

var store = new DpapiSecretStore();
var secret = "smoke-" + Guid.NewGuid();
await store.SetAsync("smoke-test", secret);
Check(await store.GetAsync("smoke-test") == secret, "DPAPI round-trip (set/get)");
await store.DeleteAsync("smoke-test");
Check(await store.GetAsync("smoke-test") is null, "DPAPI delete");

// ---- Command palette: open, type, select (fake gateway) ----
var palette = new CommandPaletteEngine();
var fakeGateway = new PaletteFakeGateway();

var paletteSkills = await CommandPaletteEngine.GetSkillsSafeAsync(fakeGateway);
var paletteSessions = await CommandPaletteEngine.GetSessionsSafeAsync(fakeGateway);
var paletteItems = palette.BuildItems(paletteSkills, paletteSessions); // open
Check(paletteItems.Count > 0, $"palette open builds rows (got {paletteItems.Count})");
Check(paletteItems.Any(i => i.Kind == PaletteItemKind.Skill && i.Title == "weather"), "palette open includes skill rows");
Check(paletteItems.Any(i => i.Kind == PaletteItemKind.Session && i.Payload == "sess_1"), "palette open includes session rows");

var typedNew = palette.Query(paletteItems, "new"); // type
Check(typedNew.Count > 0 && typedNew[0].Title == "New session", "typing 'new' ranks New session first");
var effectNew = palette.EffectFor(typedNew[0]); // select
Check(effectNew.Kind == PaletteEffectKind.NewSession, "selecting New session yields the NewSession effect");

var typedSkill = palette.Query(paletteItems, "weath");
Check(typedSkill.Count > 0 && typedSkill[0].Kind == PaletteItemKind.Skill, "typing 'weath' finds the skill");
var effectSkill = palette.EffectFor(typedSkill[0]);
Check(effectSkill.Kind == PaletteEffectKind.InsertText && effectSkill.Payload == "/weather ",
    $"selecting skill inserts '/weather ' (got '{effectSkill.Payload}')");

var typedSession = palette.Query(paletteItems, "restart");
Check(typedSession.Count > 0 && typedSession[0].Kind == PaletteItemKind.Session, "typing 'restart' finds the session");
var effectSession = palette.EffectFor(typedSession[0]);
Check(effectSession.Kind == PaletteEffectKind.OpenSession && effectSession.Payload == "sess_1",
    "selecting session yields OpenSession for sess_1");

var typedSettings = palette.Query(paletteItems, "settings");
Check(typedSettings.Any(i => i.Kind == PaletteItemKind.Page && i.Payload == "settings"),
    "typing 'settings' finds the settings page");

fakeGateway.FailSkills = true;
Check((await CommandPaletteEngine.GetSkillsSafeAsync(fakeGateway)).Count == 0,
    "skills 500 (gateway bug) degrades to empty instead of throwing");
fakeGateway.FailSessions = true;
Check((await CommandPaletteEngine.GetSessionsSafeAsync(fakeGateway)).Count == 0,
    "unreachable gateway degrades sessions to empty instead of throwing");

Console.WriteLine("--- offline queue verification ---");

// --- Offline queue runtime verification ---
// Dead port: the flush must abort and the message must persist durably.
var deadDir = Path.Combine(Path.GetTempPath(), "ataraxia-queue-dead-" + Guid.NewGuid().ToString("N"));
var deadQueue = new FileOutboundMessageQueue(deadDir);
await deadQueue.EnqueueAsync("dead-session", "queued while offline");
var deadClient = new GatewayClient(new HttpClient(), new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8649") });
var deadFlusher = new OutboundQueueFlusher(deadClient, deadQueue);
await deadFlusher.FlushAsync();
Check(await deadQueue.CountAsync() == 1, "dead-port flush aborts, message stays queued");
var reread = new FileOutboundMessageQueue(deadDir);
var rereadPending = await reread.GetPendingAsync();
Check(rereadPending.Count == 1
    && rereadPending[0].Text == "queued while offline"
    && rereadPending[0].AttemptCount == 0
    && rereadPending[0].LastError is null,
    "queued message persists across queue instances (durable JSONL)");
Directory.Delete(deadDir, recursive: true);

// Live gateway: queued message flushes through POST /api/sessions/{id}/chat
// and the queue drains. (One probe session is left behind; the client has no
// session-delete method yet and adding one is out of scope.)
var liveDir = Path.Combine(Path.GetTempPath(), "ataraxia-queue-live-" + Guid.NewGuid().ToString("N"));
var liveQueue = new FileOutboundMessageQueue(liveDir);
var liveClient = new GatewayClient(new HttpClient(), new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642") });
liveClient.SetApiKey(apiKey);
var probeSession = await liveClient.CreateSessionAsync("queue-verify-probe-" + Guid.NewGuid().ToString("N")[..8]);
await liveQueue.EnqueueAsync(probeSession.Id, "reply with the word ok");
string? assistantText = null;
var liveFlusher = new OutboundQueueFlusher(liveClient, liveQueue);
liveFlusher.MessageSent += (_, e) => assistantText = e.AssistantText;
await liveFlusher.FlushAsync();
Check(await liveQueue.CountAsync() == 0, "live flush drains the queue");
Check(assistantText?.Contains("ok", StringComparison.OrdinalIgnoreCase) == true,
    $"flushed message received an assistant reply (got '{assistantText}')");

// Unknown session id: the gateway rejects it, so the message is marked
// failed (kept, with attempt count + error) and the flush continues.
await liveQueue.EnqueueAsync("no-such-session-0000", "will fail");
QueuedMessage? failedMessage = null;
liveFlusher.MessageFailed += (_, m) => failedMessage = m;
await liveFlusher.FlushAsync();
var failedPending = await liveQueue.GetPendingAsync();
Check(failedPending.Count == 1
    && failedPending[0].AttemptCount == 1
    && failedPending[0].LastError is not null,
    $"rejected message marked failed with attempt count + error (got '{failedPending.FirstOrDefault()?.LastError}')");
Check(failedMessage is not null, "MessageFailed event raised for the rejected message");
Directory.Delete(liveDir, recursive: true);

// --- Usage meter: stream a prompt, accumulate SSE usage, fetch gateway cost ---
// Exercises the same pipeline the status-bar meter uses: run.completed usage
// -> TurnUsage.FromEvent -> UsageMeter.RecordTurn, then GET /api/sessions/{id}
// -> UsageMeter.SyncFromSession for the authoritative totals + estimated cost.
var meterClient = new GatewayClient(
    new HttpClient(),
    new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642"), ApiKey = apiKey });
var meter = new UsageMeter();
var usageSession = await meterClient.CreateSessionAsync("smoke-usage-meter");
Check(usageSession.Id.StartsWith("api_"), "usage probe session created");
meter.BeginSession(usageSession.Id, usageSession.InputTokens, usageSession.OutputTokens, usageSession.EstimatedCostUsd);
Check(meter.TotalTokens == 0 && meter.EstimatedCostUsd is null, "meter starts zeroed for a new session");

await foreach (var e in meterClient.StreamChatEventsAsync(usageSession.Id, "reply with the word ok"))
{
    using (e)
    {
        if (TurnUsage.FromEvent(e, meter.Turns.Count + 1) is { } turn)
        {
            meter.RecordTurn(turn);
            Console.WriteLine($"turn usage: in={turn.InputTokens} out={turn.OutputTokens}");
        }
    }
}
Check(meter.Turns.Count == 1, "one turn recorded from run.completed usage");
Check(meter is { InputTokens: > 0, OutputTokens: > 0 },
    $"meter accumulated {meter.InputTokens} in / {meter.OutputTokens} out");

var refreshed = await meterClient.GetSessionAsync(usageSession.Id);
Check(refreshed is not null, "GET /api/sessions/{id} returns the session");
if (refreshed is not null)
{
    meter.SyncFromSession(refreshed);
    Console.WriteLine($"session totals: in={refreshed.InputTokens} out={refreshed.OutputTokens} cost={refreshed.EstimatedCostUsd}");
    Check(meter.InputTokens == refreshed.InputTokens && meter.OutputTokens == refreshed.OutputTokens,
        "meter adopts authoritative session totals");
    Check(refreshed.EstimatedCostUsd is not null,
        "gateway reports estimated_cost_usd (no local rate table needed)");
}
await meterClient.DisposeAsync();

// Clean up the probe session via raw HTTP (no delete on the client surface).
using (var deleter = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8642") })
{
    deleter.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    using var delResponse = await deleter.DeleteAsync($"api/sessions/{usageSession.Id}");
    Check(delResponse.IsSuccessStatusCode, "probe session deleted");
}

Console.WriteLine("--- ops surface verification (live gateway) ---");

// GET /health/detailed: readiness, platform table, disk.
var opsClient = new GatewayClient(
    new HttpClient(),
    new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642"), ApiKey = apiKey });
var detailed = await opsClient.GetDetailedHealthAsync();
Check(detailed.Version == "0.21.6", $"detailed health version 0.21.6 (got '{detailed.Version}')");
Check(detailed.Status == "ok", $"detailed health status ok (got '{detailed.Status}')");
Check(detailed.Platforms.Count >= 13,
    $"platform table has >=13 entries (got {detailed.Platforms.Count})");
Check(detailed.Platforms.TryGetValue("api_server", out var apiServer) && apiServer.State == "connected",
    "api_server platform connected");
Check(apiServer?.Metrics?.MetricsToday?.Tokens > 0,
    $"api_server metrics_today tokens present (got {apiServer?.Metrics?.MetricsToday?.Tokens})");
Check(detailed.Readiness?.Checks.Disk?.FreeBytes > 0,
    $"disk free bytes present (got {detailed.Readiness?.Checks.Disk?.FreeBytes})");
Console.WriteLine($"platforms: {string.Join(", ", detailed.Platforms.Keys.Order())}");

// Full session iteration: pages until has_more is false.
var allSessions = await opsClient.GetAllSessionsAsync();
Check(allSessions.Count > 0, $"GetAllSessionsAsync iterated {allSessions.Count} sessions");

// Native diagnostics: six checks over the verified surface.
var runner = new DiagnosticsRunner(opsClient);
var report = await runner.RunAsync();
foreach (var check in report.Checks)
{
    Console.WriteLine($"diag {check.Status}: {check.Name} — {check.Detail}");
}
Check(report.Checks.Count == 6, $"diagnostics ran 6 checks (got {report.Checks.Count})");
Check(report.FailCount == 0, $"diagnostics report no failures ({report.SummaryText})");
await opsClient.DisposeAsync();

Console.WriteLine("--- Electron import dry run (read-only probe) ---");

// The importer scans the Electron desktop app's data without writing
// anything. Preview only: counts, labels, and key lengths — values are
// never printed, logged, or persisted by this program.
var importer = new ElectronImporter(
    ElectronDataPaths.Default,
    new DpapiSecretStore(),
    new JsonSettingsService());
var preview = await importer.PreviewAsync();
Console.WriteLine($"source dir: {preview.SourceDirectory}");
Console.WriteLine($"connections found: {preview.Connections.Count}");
foreach (var c in preview.Connections)
{
    Console.WriteLine($"  id='{c.Id}' kind='{c.Kind}' label='{c.Label}' url='{c.ResolvedBaseUrl ?? "<none>"}' " +
                      $"keyAvailable={c.KeyAvailable} keyLength={c.KeyLength} keySource='{c.KeySourceDescription}'");
}

Console.WriteLine($"settings found: {(preview.SettingsFound.Count == 0 ? "<none>" : string.Join("; ", preview.SettingsFound))}");
Check(preview.Connections.Count >= 1, "dry run finds at least one Electron connection");
var localPreview = preview.Connections.FirstOrDefault(c => c.Kind == "local");
Check(localPreview?.ResolvedBaseUrl == "http://127.0.0.1:8642",
    $"local connection resolves to loopback (got '{localPreview?.ResolvedBaseUrl}')");
Check(localPreview?.KeyAvailable == true,
    "dry run finds a migratable API key for the local connection (length only, value never printed)");

// Sessions live gateway-side: the native client fetches them via
// GET /api/sessions — no session file migration is built or needed.
await using (var sessionsClient = new GatewayClient(
    new HttpClient(),
    new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642"), ApiKey = apiKey }))
{
    var page = await sessionsClient.GetSessionsPageAsync(limit: 5);
    Console.WriteLine($"gateway sessions (first page): {page.Sessions.Count} shown, hasMore={page.HasMore}");
    Check(page.Sessions.Count > 0, "GET /api/sessions returns live sessions — no file migration needed");
}

Console.WriteLine("--- toolsets + skills endpoint verification (2026-10-11) ---");

// GET /v1/toolsets is live: name/label/description/enabled/configured/tools.
await using (var toolsetsClient = new GatewayClient(
    new HttpClient(),
    new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642"), ApiKey = apiKey }))
{
    var toolsets = await toolsetsClient.GetToolsetsAsync();
    Console.WriteLine($"toolsets: {toolsets.Count}");
    foreach (var t in toolsets.Take(6))
    {
        Console.WriteLine($"  {t.Name} enabled={t.Enabled} configured={t.Configured} tools={t.Tools.Count}");
    }
    Check(toolsets.Count > 0, "GET /v1/toolsets returns live toolsets");
    Check(toolsets.All(t => !string.IsNullOrWhiteSpace(t.Name)), "every toolset has a name");

    // GET /v1/skills: the gateway 500s today (server-side; the route-table
    // handler catches Exception -> 500). The client must surface a typed
    // GatewayApiException, not hang or return junk. Not a hard failure: the
    // bug is gateway-side and may be fixed independently of this client.
    try
    {
        var skills = await toolsetsClient.GetSkillsAsync();
        Console.WriteLine($"skills: {skills.Count} (endpoint healthy again)");
    }
    catch (GatewayApiException ex) when (ex.StatusCode == 500)
    {
        Console.WriteLine("skills: HTTP 500 (gateway-side bug) surfaced as GatewayApiException — client degrades cleanly");
    }
}

Console.WriteLine("--- cron jobs surface (verified live 2026-10-11) ---");

await using (var cronClient = new GatewayClient(
    new HttpClient(),
    new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642"), ApiKey = apiKey }))
{
    // GET /api/jobs must exist and return {"jobs": [...]}.
    var jobs = await cronClient.GetCronJobsAsync(includeDisabled: true);
    Check(jobs.Jobs is not null, "GET /api/jobs returns a jobs list");

    // Blocked-with-evidence: these live on the desktop serve backend only
    // (verified 404 on 8642, 2026-10-11) — no UI is built against them.
    using (var blocker = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8642") })
    {
        blocker.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        foreach (var blocked in new[] { "api/messaging/platforms", "api/providers", "api/mcp/servers", "api/profiles" })
        {
            using var blockedResponse = await blocker.GetAsync(blocked);
            Check(blockedResponse.StatusCode == System.Net.HttpStatusCode.NotFound,
                $"GET /{blocked} -> 404 (serve-backend-only, no client surface)");
        }
    }

    // Full CRUD round-trip on a paused job: created paused so it never arms,
    // renamed, re-paused, then deleted. POST /run is deliberately skipped —
    // it would execute a prompt on the live gateway.
    var probeName = "smoke-probe-" + Guid.NewGuid().ToString("N")[..8];
    var created = await cronClient.CreateCronJobAsync(
        new CronJobCreateRequest(probeName, "0 0 1 1 *", "smoke test probe", Paused: true));
    Check(System.Text.RegularExpressions.Regex.IsMatch(created.Id, "^[a-f0-9]{12}$"),
        $"created job id is 12 lowercase hex (got '{created.Id}')");
    Check(!created.Enabled && created.State == "paused", "created job is paused (never armed)");

    var fetched = await cronClient.GetCronJobAsync(created.Id);
    Check(fetched.Name == probeName, "GET /api/jobs/{id} returns the probe job");

    var renamed = await cronClient.UpdateCronJobAsync(created.Id, new CronJobUpdateRequest(Name: probeName + "-renamed"));
    Check(renamed.Name == probeName + "-renamed", "PATCH renames the probe job");

    var paused = await cronClient.PauseCronJobAsync(created.Id);
    Check(!paused.Enabled, "POST /pause keeps the probe job paused");

    var deleted = await cronClient.DeleteCronJobAsync(created.Id);
    Check(deleted, "DELETE removes the probe job");
}

Console.WriteLine("--- agent runs verification (live, one tiny turn) ---");

// POST /v1/runs -> 202 with the run id; the run executes asynchronously.
var runClient = new GatewayClient(
    new HttpClient(),
    new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642"), ApiKey = apiKey });
var runCreated = await runClient.CreateRunAsync(
    "reply with exactly the word ok and nothing else",
    idempotencyKey: "smoke-" + Guid.NewGuid().ToString("N"));
Check(runCreated.RunId.StartsWith("run_"), $"run created ({runCreated.RunId})");
Check(runCreated.Status is "started" or "queued", $"create status is '{runCreated.Status}'");

// Event stream: collect until the terminal event (bounded).
var runEvents = new List<RunEvent>();
using (var eventsCts = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
{
    try
    {
        await foreach (var e in runClient.StreamRunEventsAsync(runCreated.RunId, null, eventsCts.Token))
        {
            runEvents.Add(e);
            if (e.IsTerminal)
            {
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Bounded read; the poll loop below still verifies the terminal state.
    }
}

var completedEvent = runEvents.FirstOrDefault(e => e.Name == "run.completed");
Check(completedEvent is not null, $"event stream reached run.completed ({runEvents.Count} events)");
Check(runEvents.Select(e => e.Seq).SequenceEqual(runEvents.Select(e => e.Seq).OrderBy(s => s)),
    "event seqs are ordered");
Check(completedEvent?.Output?.Contains("ok", StringComparison.OrdinalIgnoreCase) == true,
    $"run output contains 'ok' (got '{completedEvent?.Output}')");
Check(completedEvent?.Usage is { InputTokens: > 0, OutputTokens: > 0 },
    "run.completed carries token usage");

// Polling: GET /v1/runs/{id} reaches a terminal status.
RunStatus? terminalStatus = null;
for (var i = 0; i < 30 && terminalStatus is not { IsTerminal: true }; i++)
{
    terminalStatus = await runClient.GetRunAsync(runCreated.RunId);
    if (!terminalStatus.IsTerminal)
    {
        await Task.Delay(2000);
    }
}
Check(terminalStatus?.IsTerminal == true,
    $"polling reached terminal status '{terminalStatus?.Status}'");
Check(terminalStatus?.Usage?.InputTokens > 0, "polled status carries usage");

// Approval gate: a terminal run has no active approval session -> 409.
try
{
    await runClient.ResolveApprovalAsync(runCreated.RunId, "deny");
    Check(false, "approval on a terminal run is refused");
}
catch (GatewayApiException ex)
{
    Check(ex.StatusCode == 409 && ex.ErrorCode == "approval_not_active",
        $"approval on terminal run -> 409 approval_not_active (got {ex.StatusCode}/{ex.ErrorCode})");
}

// Fork: POST /api/sessions/{id}/fork -> 201 with parent_session_id.
var forkParent = await runClient.CreateSessionAsync("smoke-fork-parent-" + Guid.NewGuid().ToString("N")[..8]);
var forkChild = await runClient.ForkSessionAsync(forkParent.Id, "smoke-fork-child");
Check(forkChild.ParentSessionId == forkParent.Id,
    $"fork carries parent_session_id (got '{forkChild.ParentSessionId}')");

// Clean up the fork probe sessions via raw HTTP.
using (var forkDeleter = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:8642") })
{
    forkDeleter.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    Check((await forkDeleter.DeleteAsync($"api/sessions/{forkChild.Id}")).IsSuccessStatusCode,
        "probe fork deleted");
    Check((await forkDeleter.DeleteAsync($"api/sessions/{forkParent.Id}")).IsSuccessStatusCode,
        "probe fork parent deleted");
}
await runClient.DisposeAsync();

Console.WriteLine(failures.Count == 0 ? "SMOKE OK" : $"SMOKE FAILED ({failures.Count})");
return failures.Count == 0 ? 0 : 1;

static string? ReadApiServerKey()
{
    var env = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "hermes", ".env");
    foreach (var line in File.ReadLines(env))
    {
        if (line.StartsWith("API_SERVER_KEY=", StringComparison.Ordinal))
        {
            return line["API_SERVER_KEY=".Length..].Trim();
        }
    }

    return null;
}

// Fake gateway for the command-palette checks: serves one skill + one
// session, and can simulate the live /v1/skills 500 or a dead gateway.
sealed class PaletteFakeGateway : IGatewayClient
{
    public bool FailSkills { get; set; }
    public bool FailSessions { get; set; }

    public Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default) =>
        FailSkills
            ? throw new GatewayApiException(500, "Failed to enumerate skills", "server_error", null)
            : Task.FromResult<IReadOnlyList<SkillInfo>>([new SkillInfo("weather", "weather", "Current weather lookup")]);

    public Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        FailSessions
            ? throw new HttpRequestException("No connection could be made")
            : Task.FromResult<IReadOnlyList<Session>>(
                [new Session("sess_1", "Fix restart bugs", null, 12, "openai/gpt-5", null, null, null, false, false)]);

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
    public Task<IReadOnlyList<ToolsetInfo>> GetToolsetsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<RunCreateResponse> CreateRunAsync(string input, string? sessionId = null, string? model = null, string? idempotencyKey = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<RunStatus> GetRunAsync(string runId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public IAsyncEnumerable<RunEvent> StreamRunEventsAsync(string runId, long? lastSeq = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<SteerResponse> SteerRunAsync(string runId, string message, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<RunStatus> StopRunAsync(string runId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<Session> ForkSessionAsync(string sessionId, string? title = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CronJobList> GetCronJobsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CronJob> GetCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CronJob> CreateCronJobAsync(CronJobCreateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CronJob> UpdateCronJobAsync(string jobId, CronJobUpdateRequest update, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> DeleteCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CronJob> PauseCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CronJob> ResumeCronJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CronJob> RunCronJobAsync(string jobId, string? extraPrompt = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DetailedHealth> GetDetailedHealthAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<Session>> GetAllSessionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
