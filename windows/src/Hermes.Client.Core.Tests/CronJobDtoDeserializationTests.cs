using Xunit;
using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Deserialization tests for the cron DTOs (<c>Dtos.CronJobs.cs</c>): the full
/// stored job record from <c>cron/jobs.py</c> maps onto <see cref="CronJob"/>,
/// missing optionals stay null, and unknown future fields are tolerated.
/// </summary>
public sealed class CronJobDtoDeserializationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string FullJob = """
        {"id": "abc123def456", "name": "morning brief", "prompt": "Summarize overnight events.",
         "skills": ["briefing"], "skill": "briefing", "model": "gpt-5", "provider": "openai",
         "base_url": null, "script": null, "no_agent": false,
         "schedule": {"kind": "interval", "minutes": 30, "display": "every 30m"},
         "schedule_display": "every 30m",
         "repeat": {"times": 5, "completed": 2},
         "enabled": false, "state": "paused",
         "paused_at": "2026-10-11T06:00:00+00:00", "paused_reason": "operator hold",
         "created_at": "2026-10-01T00:00:00+00:00", "next_run_at": null,
         "last_run_at": "2026-10-11T05:30:00+00:00", "last_status": "error",
         "last_error": "model timeout", "last_delivery_error": null,
         "last_failure": {"at": "2026-10-11T05:30:00+00:00", "detail": "model timeout"},
         "failure_streak": 2, "deliver": "telegram", "workdir": null,
         "future_field_the_gateway_may_add": {"nested": true}}
        """;

    [Fact]
    public void CronJob_MapsFullRecord()
    {
        var job = JsonSerializer.Deserialize<CronJob>(FullJob, Json)!;

        Assert.Equal("abc123def456", job.Id);
        Assert.Equal("morning brief", job.Name);
        Assert.Equal(["briefing"], job.Skills);
        Assert.Equal("interval", job.Schedule?.Kind);
        Assert.Equal("every 30m", job.Schedule?.Display);
        Assert.Equal("every 30m", job.ScheduleDisplay);
        Assert.Equal(5, job.Repeat?.Times);
        Assert.Equal(2, job.Repeat?.Completed);
        Assert.False(job.Enabled);
        Assert.Equal("paused", job.State);
        Assert.Equal("operator hold", job.PausedReason);
        Assert.Equal(2, job.FailureStreak);
        Assert.Equal("telegram", job.Deliver);
        Assert.Equal("model timeout", job.LastFailure?.Detail);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), job.CreatedAt);
        Assert.Null(job.NextRunAt);
    }

    [Fact]
    public void CronJob_ToleratesMinimalRecord()
    {
        const string minimal = """
            {"id": "000000000001", "name": "j", "enabled": true, "failure_streak": 0,
             "skills": []}
            """;

        var job = JsonSerializer.Deserialize<CronJob>(minimal, Json)!;

        Assert.Equal("000000000001", job.Id);
        Assert.True(job.Enabled);
        Assert.Null(job.Prompt);
        Assert.Null(job.Schedule);
        Assert.Null(job.Repeat);
        Assert.Empty(job.Skills);
    }

    [Fact]
    public void CronJobList_MapsJobsArray()
    {
        var list = JsonSerializer.Deserialize<CronJobList>("""{"jobs": [""" + FullJob + "]}", Json)!;

        Assert.Equal("abc123def456", Assert.Single(list.Jobs).Id);
    }

    [Fact]
    public void CronJobCreateRequest_SerializesSnakeCase()
    {
        var json = JsonSerializer.Serialize(
            new CronJobCreateRequest("n", "0 7 * * *", "p", Skills: ["a"], Paused: true), Json);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("n", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal("0 7 * * *", doc.RootElement.GetProperty("schedule").GetString());
        Assert.True(doc.RootElement.GetProperty("paused").GetBoolean());
        Assert.Equal("a", doc.RootElement.GetProperty("skills")[0].GetString());
        // Null optionals are omitted (WhenWritingNull), keeping the POST body lean.
        Assert.False(doc.RootElement.TryGetProperty("repeat", out _));
        Assert.False(doc.RootElement.TryGetProperty("deliver", out _));
    }
}
