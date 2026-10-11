using Xunit;
using System.Net;
using System.Text;
using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// HTTP-level tests for the cron surface (<c>GatewayClient.CronJobs.cs</c>):
/// <c>GET/POST /api/jobs</c>, <c>GET/PATCH/DELETE /api/jobs/{id}</c>, and
/// <c>POST /api/jobs/{id}/{pause,resume,run}</c>. Shapes verified against the
/// gateway source (<c>gateway/platforms/api_server.py</c>, 2026-10-11) and the
/// live 8642 endpoint. A stub <see cref="HttpMessageHandler"/> keeps it offline.
/// </summary>
public sealed class GatewayClientCronJobsTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }

    private static GatewayClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642") });

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    // The gateway's stored job record (cron/jobs.py create_job), trimmed to
    // the fields the DTO models.
    private const string JobJson = """
        {"id": "abc123def456", "name": "morning brief", "prompt": "Summarize overnight events.",
         "skills": ["briefing"], "skill": "briefing", "model": null, "provider": null,
         "schedule": {"kind": "cron", "expr": "0 7 * * *", "display": "0 7 * * *"},
         "schedule_display": "0 7 * * *",
         "repeat": {"times": null, "completed": 3},
         "enabled": true, "state": "scheduled",
         "paused_at": null, "paused_reason": null,
         "created_at": "2026-10-10T07:00:00+00:00",
         "next_run_at": "2026-10-12T07:00:00+00:00",
         "last_run_at": "2026-10-11T07:00:00+00:00",
         "last_status": "ok", "last_error": null, "last_failure": null,
         "failure_streak": 0, "deliver": "local"}
        """;

    [Fact]
    public async Task GetCronJobsAsync_ParsesLiveListShape()
    {
        var requested = new List<string>();
        var client = ClientFor(req =>
        {
            requested.Add(req.Method + " " + req.RequestUri!.PathAndQuery);
            return Json("""{"jobs": [""" + JobJson + "]}");
        });

        var list = await client.GetCronJobsAsync();

        Assert.Equal("GET /api/jobs", Assert.Single(requested));
        var job = Assert.Single(list.Jobs);
        Assert.Equal("abc123def456", job.Id);
        Assert.Equal("morning brief", job.Name);
        Assert.Equal("0 7 * * *", job.ScheduleDisplay);
        Assert.Equal("cron", job.Schedule?.Kind);
        Assert.True(job.Enabled);
        Assert.Equal("scheduled", job.State);
        Assert.Equal(3, job.Repeat?.Completed);
        Assert.Null(job.Repeat?.Times);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 7, 0, 0, TimeSpan.Zero), job.NextRunAt);
    }

    [Fact]
    public async Task GetCronJobsAsync_IncludeDisabled_SendsQueryFlag()
    {
        var requested = new List<string>();
        var client = ClientFor(req =>
        {
            requested.Add(req.RequestUri!.PathAndQuery);
            return Json("""{"jobs": []}""");
        });

        await client.GetCronJobsAsync(includeDisabled: true);

        Assert.Equal("/api/jobs?include_disabled=true", Assert.Single(requested));
    }

    [Fact]
    public async Task GetCronJobAsync_UsesJobPath()
    {
        var requested = new List<string>();
        var client = ClientFor(req =>
        {
            requested.Add(req.Method + " " + req.RequestUri!.PathAndQuery);
            return Json("""{"job": """ + JobJson + "}");
        });

        var job = await client.GetCronJobAsync("abc123def456");

        Assert.Equal("GET /api/jobs/abc123def456", Assert.Single(requested));
        Assert.Equal("morning brief", job.Name);
    }

    [Fact]
    public async Task CreateCronJobAsync_PostsCreateBody()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var client = ClientFor(req =>
        {
            captured = req;
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{"job": """ + JobJson + "}");
        });

        var job = await client.CreateCronJobAsync(
            new CronJobCreateRequest("morning brief", "0 7 * * *", "Summarize overnight events."));

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/jobs", captured.RequestUri!.PathAndQuery);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("morning brief", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("0 7 * * *", doc.RootElement.GetProperty("schedule").GetString());
        Assert.Equal("Summarize overnight events.", doc.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("abc123def456", job.Id);
    }

    [Fact]
    public async Task UpdateCronJobAsync_PatchesOnlySuppliedFields()
    {
        string? capturedBody = null;
        var client = ClientFor(req =>
        {
            Assert.Equal(HttpMethod.Patch, req.Method);
            Assert.Equal("/api/jobs/abc123def456", req.RequestUri!.PathAndQuery);
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{"job": """ + JobJson + "}");
        });

        await client.UpdateCronJobAsync("abc123def456", new CronJobUpdateRequest(Name: "renamed"));

        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("renamed", doc.RootElement.GetProperty("name").GetString());
        Assert.False(doc.RootElement.TryGetProperty("schedule", out _));
        Assert.False(doc.RootElement.TryGetProperty("prompt", out _));
    }

    [Fact]
    public async Task DeleteCronJobAsync_ReturnsOk()
    {
        var requested = new List<string>();
        var client = ClientFor(req =>
        {
            requested.Add(req.Method + " " + req.RequestUri!.PathAndQuery);
            return Json("""{"ok": true}""");
        });

        var ok = await client.DeleteCronJobAsync("abc123def456");

        Assert.True(ok);
        Assert.Equal("DELETE /api/jobs/abc123def456", Assert.Single(requested));
    }

    [Fact]
    public async Task PauseResumeRunCronJobAsync_PostToSubpaths()
    {
        var requested = new List<string>();
        string? runBody = null;
        var client = ClientFor(req =>
        {
            requested.Add(req.Method + " " + req.RequestUri!.PathAndQuery);
            if (req.RequestUri!.PathAndQuery.EndsWith("/run"))
            {
                runBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            }

            return Json("""{"job": """ + JobJson + "}");
        });

        await client.PauseCronJobAsync("abc123def456");
        await client.ResumeCronJobAsync("abc123def456");
        await client.RunCronJobAsync("abc123def456", extraPrompt: "one more time");

        Assert.Equal(
            ["POST /api/jobs/abc123def456/pause",
             "POST /api/jobs/abc123def456/resume",
             "POST /api/jobs/abc123def456/run"],
            requested);
        using var doc = JsonDocument.Parse(runBody!);
        Assert.Equal("one more time", doc.RootElement.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task GetCronJobAsync_ThrowsOnNotFound()
    {
        var client = ClientFor(_ => Json("""{"error": "Job not found"}""", HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<GatewayApiException>(() => client.GetCronJobAsync("abc123def456"));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task RunCronJobAsync_ThrowsNotFoundForUnknownJob()
    {
        // Regression guard (2026-10-11): the live gateway answers 404 here
        // only for an unknown job id — the /run route itself always exists
        // (verified against api_server.py _http_route_table + _job_response).
        // A 404 must surface as GatewayApiException(404), never be mistaken
        // for a missing endpoint.
        var requested = new List<string>();
        var client = ClientFor(req =>
        {
            requested.Add(req.Method + " " + req.RequestUri!.PathAndQuery);
            return Json("""{"error": "Job not found"}""", HttpStatusCode.NotFound);
        });

        var ex = await Assert.ThrowsAsync<GatewayApiException>(
            () => client.RunCronJobAsync("nope-not-a-real-id"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("POST /api/jobs/nope-not-a-real-id/run", Assert.Single(requested));
    }
}
