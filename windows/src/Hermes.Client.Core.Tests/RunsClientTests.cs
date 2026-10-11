using Xunit;
using System.Net;
using System.Text;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// HTTP-level tests for the /v1/runs surface (<c>GatewayClient.Runs.cs</c>):
/// run creation (202), status polling, SSE event streaming, steer/stop,
/// and session forking. A stub <see cref="HttpMessageHandler"/> keeps
/// everything offline; wire shapes mirror the gateway verified 2026-10-11.
/// </summary>
public sealed class RunsClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = req => Task.FromResult(respond(req));

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            _respond(request);
    }

    private static GatewayClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642") });

    private static GatewayClient ClientForAsync(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642") });

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Sse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };

    [Fact]
    public async Task CreateRunAsyncPostsInputAndParses202()
    {
        HttpRequestMessage? seen = null;
        string? seenBody = null;
        var client = ClientForAsync(async req =>
        {
            seen = req;
            seenBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Json("""{"run_id": "run_abc123", "status": "started", "replayed": false}""",
                HttpStatusCode.Accepted);
        });

        var created = await client.CreateRunAsync("reply with the word ok");

        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("/v1/runs", seen.RequestUri!.AbsolutePath);
        Assert.Equal("run_abc123", created.RunId);
        Assert.Equal("started", created.Status);
        Assert.False(created.Replayed);
        var body = seenBody;
        Assert.Contains("reply with the word ok", body);
    }

    [Fact]
    public async Task CreateRunAsyncSendsSessionModelAndIdempotencyKey()
    {
        HttpRequestMessage? seen = null;
        string? seenBody = null;
        var client = ClientForAsync(async req =>
        {
            seen = req;
            seenBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
            return Json("""{"run_id": "run_x", "status": "started", "replayed": true}""",
                HttpStatusCode.Accepted);
        });

        var created = await client.CreateRunAsync(
            "hi", sessionId: "sess_1", model: "m", idempotencyKey: "key-1");

        var body = seenBody;
        Assert.Contains("\"session_id\":\"sess_1\"", body!);
        Assert.Contains("\"model\":\"m\"", body!);
        Assert.Equal("key-1", seen!.Headers.GetValues("Idempotency-Key").Single());
        Assert.True(created.Replayed);
    }

    [Fact]
    public async Task GetRunAsyncParsesFullStatus()
    {
        const string body = """
            {"object": "hermes.run", "run_id": "run_1", "status": "completed",
             "created_at": 1791742092.266, "updated_at": 1791742102.256,
             "session_id": "run_1", "model": "hermes-agent",
             "last_event": "run.completed", "output": "ok",
             "usage": {"input_tokens": 29084, "output_tokens": 5, "total_tokens": 29089,
                       "cache_read_tokens": 28160, "cache_write_tokens": 0},
             "runtime": {"provider": "openai-codex", "model": "gpt-5.6-terra", "route_source": "global"},
             "completed": true, "partial": false, "interrupted": false}
            """;
        var client = ClientFor(req => Json(body));

        var status = await client.GetRunAsync("run_1");

        Assert.Equal("run_1", status.RunId);
        Assert.Equal("completed", status.Status);
        Assert.True(status.IsTerminal);
        Assert.False(status.IsWaitingForApproval);
        Assert.Equal("ok", status.Output);
        Assert.Equal(29084, status.Usage!.InputTokens);
        Assert.Equal(5, status.Usage.OutputTokens);
        Assert.Equal("openai-codex", status.Runtime!.Provider);
        Assert.Equal("run.completed", status.LastEvent);
        Assert.NotNull(status.CreatedAt);
        Assert.True(status.UpdatedAt > status.CreatedAt);
    }

    [Fact]
    public async Task GetRunAsyncWaitingForApprovalSurfacesApproval()
    {
        const string body = """
            {"object": "hermes.run", "run_id": "run_2", "status": "waiting_for_approval",
             "created_at": 1791742092.0, "updated_at": 1791742099.0,
             "session_id": "run_2", "model": "hermes-agent"}
            """;
        var client = ClientFor(_ => Json(body));

        var status = await client.GetRunAsync("run_2");

        Assert.True(status.IsWaitingForApproval);
        Assert.False(status.IsTerminal);
    }

    [Fact]
    public async Task GetRunAsyncThrows404WithCode()
    {
        var client = ClientFor(_ => Json(
            """{"error": {"message": "Run not found: nope", "type": "invalid_request_error", "code": "run_not_found"}}""",
            HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<GatewayApiException>(() => client.GetRunAsync("nope"));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("run_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task StreamRunEventsAsyncParsesDataEventNames()
    {
        // The gateway puts the event name in data.event (no SSE "event:" line).
        const string sse = """
            : open

            id: 0
            data: {"event": "run.steered", "run_id": "run_1", "timestamp": 1791742098.3, "accepted": true, "seq": 0}

            id: 1
            data: {"event": "message.delta", "run_id": "run_1", "timestamp": 1791742102.0, "delta": "ok", "seq": 1}

            id: 2
            data: {"event": "run.completed", "run_id": "run_1", "timestamp": 1791742102.2, "output": "ok", "seq": 2,
                   "usage": {"input_tokens": 10, "output_tokens": 1, "total_tokens": 11}}

            : stream closed

            """;
        var client = ClientFor(_ => Sse(sse));

        var events = new List<RunEvent>();
        await foreach (var e in client.StreamRunEventsAsync("run_1"))
        {
            events.Add(e);
        }

        Assert.Equal(3, events.Count);
        Assert.Equal("run.steered", events[0].Name);
        Assert.True(events[0].Accepted);
        Assert.Equal("message.delta", events[1].Name);
        Assert.Equal("ok", events[1].Delta);
        Assert.Equal("run.completed", events[2].Name);
        Assert.True(events[2].IsTerminal);
        Assert.Equal("ok", events[2].Output);
        Assert.Equal(10, events[2].Usage!.InputTokens);
        Assert.Equal([0, 1, 2], events.Select(e => e.Seq).ToArray());
        Assert.All(events, e => Assert.Equal("run_1", e.RunId));
        Assert.All(events, e => Assert.True(e.Timestamp > DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task StreamRunEventsAsyncParsesApprovalRequest()
    {
        const string sse = """
            id: 7
            data: {"event": "approval.request", "run_id": "run_1", "timestamp": 1791742100.5, "seq": 7,
                   "tool_name": "shell", "command": "del C:\\x", "description": "delete a file",
                   "request_id": "req-9", "message_id": "m-3",
                   "choices": ["once", "session", "always", "deny"]}

            """;
        var client = ClientFor(_ => Sse(sse));

        RunEvent? approval = null;
        await foreach (var e in client.StreamRunEventsAsync("run_1"))
        {
            approval = e;
        }

        Assert.NotNull(approval);
        Assert.True(approval.IsApprovalRequest);
        var card = approval.ToApprovalRequest();
        Assert.Equal("run_1", card.RunId);
        Assert.Equal("shell", card.ToolName);
        Assert.Equal("del C:\\x", card.Command);
        Assert.Equal("req-9", card.RequestId);
        Assert.Equal(["once", "session", "always", "deny"], card.Choices);
        Assert.True(card.HasDescription);
    }

    [Fact]
    public async Task StreamRunEventsAsyncSendsLastSeq()
    {
        HttpRequestMessage? seen = null;
        var client = ClientFor(req =>
        {
            seen = req;
            return Sse(": stream closed\n\n");
        });

        await foreach (var _ in client.StreamRunEventsAsync("run_9", lastSeq: 41))
        {
        }

        Assert.Contains("last_seq=41", seen!.RequestUri!.Query);
    }

    [Fact]
    public async Task SteerRunAsyncParsesAccepted()
    {
        HttpRequestMessage? seen = null;
        string? seenBody = null;
        var client = ClientFor(req =>
        {
            seen = req;
            seenBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{"object": "hermes.run.steer", "run_id": "run_1", "accepted": true}""");
        });

        var result = await client.SteerRunAsync("run_1", "slow down");

        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("/v1/runs/run_1/steer", seen.RequestUri!.AbsolutePath);
        Assert.True(result.Accepted);
        Assert.Equal("run_1", result.RunId);
        Assert.Contains("slow down", seenBody);
    }

    [Fact]
    public async Task SteerRunAsyncThrows409WhenNotRunning()
    {
        var client = ClientFor(_ => Json(
            """{"error": {"message": "not accepting", "type": "invalid_request_error", "code": "run_not_accepting_steer"}}""",
            HttpStatusCode.Conflict));

        var ex = await Assert.ThrowsAsync<GatewayApiException>(() => client.SteerRunAsync("run_1", "x"));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("run_not_accepting_steer", ex.ErrorCode);
    }

    [Fact]
    public async Task StopRunAsyncParsesStopping()
    {
        var client = ClientFor(_ => Json("""{"run_id": "run_1", "status": "stopping"}"""));

        var status = await client.StopRunAsync("run_1");

        Assert.Equal("run_1", status.RunId);
        Assert.Equal("stopping", status.Status);
        Assert.False(status.IsTerminal);
    }

    [Fact]
    public async Task StopRunAsyncReturnsFullStatusWhenAlreadyTerminal()
    {
        var client = ClientFor(_ => Json(
            """
            {"object": "hermes.run", "run_id": "run_1", "status": "completed",
             "created_at": 1.0, "updated_at": 2.0, "output": "ok",
             "completed": true, "partial": false, "interrupted": false}
            """));

        var status = await client.StopRunAsync("run_1");

        Assert.True(status.IsTerminal);
        Assert.Equal("ok", status.Output);
    }

    [Fact]
    public async Task ResolveApprovalAsyncSendsChoiceAndRequestId()
    {
        HttpRequestMessage? seen = null;
        string? seenBody = null;
        var client = ClientFor(req =>
        {
            seen = req;
            seenBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""
                {"object": "hermes.run.approval_response", "run_id": "run_1",
                 "choice": "once", "request_id": "req-7", "resolved": 1}
                """);
        });

        var resolution = await client.ResolveApprovalAsync("run_1", "once", "req-7");

        Assert.Equal("/v1/runs/run_1/approval", seen!.RequestUri!.AbsolutePath);
        var body = seenBody;
        Assert.Contains("\"choice\":\"once\"", body);
        Assert.Contains("\"request_id\":\"req-7\"", body);
        Assert.Equal("once", resolution.Choice);
        Assert.Equal(1, resolution.Resolved);
        Assert.Equal("req-7", resolution.RequestId);
    }

    [Fact]
    public async Task ResolveApprovalAsyncThrows409WhenNotPending()
    {
        var client = ClientFor(_ => Json(
            """{"error": {"message": "no pending approval", "type": "invalid_request_error", "code": "approval_not_pending"}}""",
            HttpStatusCode.Conflict));

        var ex = await Assert.ThrowsAsync<GatewayApiException>(
            () => client.ResolveApprovalAsync("run_1", "deny"));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("approval_not_pending", ex.ErrorCode);
    }

    [Fact]
    public async Task ForkSessionAsyncParsesParentRef()
    {
        const string body = """
            {"object": "hermes.session",
             "session": {"id": "sess_fork", "title": "probe-fork", "message_count": 2,
                         "pinned": false, "archived": false,
                         "parent_session_id": "sess_parent"}}
            """;
        HttpRequestMessage? seen = null;
        var client = ClientFor(req =>
        {
            seen = req;
            return Json(body, HttpStatusCode.Created);
        });

        var fork = await client.ForkSessionAsync("sess_parent", "probe-fork");

        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Equal("/api/sessions/sess_parent/fork", seen.RequestUri!.AbsolutePath);
        Assert.Equal("sess_fork", fork.Id);
        Assert.Equal("sess_parent", fork.ParentSessionId);
    }

    [Fact]
    public void RunStatusTerminalStates()
    {
        RunStatus Status(string s) => new("r", s, null, null, null, null, null, null, null, null, false, false, false, null);
        Assert.True(Status("completed").IsTerminal);
        Assert.True(Status("failed").IsTerminal);
        Assert.True(Status("cancelled").IsTerminal);
        Assert.False(Status("running").IsTerminal);
        Assert.False(Status("stopping").IsTerminal);
        Assert.False(Status("waiting_for_approval").IsTerminal);
        Assert.False(Status("queued").IsTerminal);
    }
}
