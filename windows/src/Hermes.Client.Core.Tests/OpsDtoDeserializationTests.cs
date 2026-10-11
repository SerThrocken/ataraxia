using Xunit;
using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Verifies the <c>GET /health/detailed</c> wire shape against the live
/// gateway (captured 2026-10-11, gateway 0.21.6). Platforms arrive as a
/// NAME-KEYED OBJECT, not a list; readiness checks have per-check shapes.
/// </summary>
public sealed class OpsDtoDeserializationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const string DetailedJson = """
        {
          "status": "ok",
          "readiness": {
            "status": "ok",
            "checks": {
              "state_db": {"status": "ok"},
              "session_store": {"status": "ok"},
              "config": {"status": "ok"},
              "model": {"status": "ok"},
              "disk": {"status": "ok", "used_percent": 25.1, "free_bytes": 1497278910464},
              "gateway": {"status": "ok", "state": "running", "connected_platforms": 13, "platforms": 13},
              "background_queues": {"status": "ok", "active_api_runs": 0, "process_completions": 0, "active_delegations": 0}
            }
          },
          "platform": "hermes-agent",
          "version": "0.21.6",
          "gateway_state": "running",
          "platforms": {
            "api_server": {
              "state": "connected", "error_code": null, "error_message": null,
              "updated_at": "2026-10-11T18:03:37.877947+00:00",
              "writer_pid": 31620, "writer_start_time": 179173149276,
              "needs_attention": false, "retrying_since": null,
              "listener_base": "http://127.0.0.1:8642",
              "metrics": {
                "host": "0.0.0.0", "port": 8642, "active_runs": 0, "stored_runs": 14,
                "last_request_at": "2026-10-11T17:44:09.439609+00:00",
                "last_heartbeat": "2026-10-11T18:03:46.952590+00:00",
                "metrics_today": {"day": "2026-10-11", "requests": 18, "messages": 18, "tokens": 523530, "latency_p95_ms": 21258.96}
              }
            },
            "coding-agent:a2a": {
              "state": "connected", "error_code": null, "error_message": null,
              "updated_at": "2026-10-11T15:12:08.214795+00:00",
              "writer_pid": 31620, "writer_start_time": 179173149276,
              "needs_attention": false, "retrying_since": null
            },
            "a2a": {
              "state": "degraded", "error_code": "timeout", "error_message": "heartbeat missed",
              "updated_at": "2026-10-11T15:12:04.499001+00:00",
              "writer_pid": 31620, "writer_start_time": 179173149276,
              "needs_attention": true, "retrying_since": "2026-10-11T15:10:00+00:00"
            }
          }
        }
        """;

    [Fact]
    public void DetailedHealthDeserializesTopLevel()
    {
        var health = JsonSerializer.Deserialize<DetailedHealth>(DetailedJson, Json)!;

        Assert.Equal("ok", health.Status);
        Assert.Equal("hermes-agent", health.Platform);
        Assert.Equal("0.21.6", health.Version);
        Assert.Equal("running", health.GatewayState);
    }

    [Fact]
    public void ReadinessChecksKeepTheirShapes()
    {
        var health = JsonSerializer.Deserialize<DetailedHealth>(DetailedJson, Json)!;
        var checks = health.Readiness!.Checks;

        Assert.Equal("ok", checks.Disk!.Status);
        Assert.Equal(25.1, checks.Disk.UsedPercent, precision: 1);
        Assert.Equal(1497278910464L, checks.Disk.FreeBytes);
        Assert.Equal(13, checks.Gateway!.ConnectedPlatforms);
        Assert.Equal("ok", checks.BackgroundQueues!.Status);
        Assert.Equal(0, checks.BackgroundQueues.ActiveApiRuns);
    }

    [Fact]
    public void PlatformsDeserializeAsNameKeyedEntries()
    {
        var health = JsonSerializer.Deserialize<DetailedHealth>(DetailedJson, Json)!;

        Assert.Equal(3, health.Platforms.Count);
        var api = health.Platforms["api_server"];
        Assert.Equal("connected", api.State);
        Assert.False(api.NeedsAttention);
        Assert.Equal(31620, api.WriterPid);
        Assert.Equal(
            DateTimeOffset.Parse("2026-10-11T18:03:37.877947+00:00"),
            api.UpdatedAt);
        Assert.Equal("http://127.0.0.1:8642", api.ListenerBase);
    }

    [Fact]
    public void ApiServerMetricsTodayDeserializes()
    {
        var health = JsonSerializer.Deserialize<DetailedHealth>(DetailedJson, Json)!;
        var metrics = health.Platforms["api_server"].Metrics!;

        Assert.Equal(8642, metrics.Port);
        Assert.Equal(14, metrics.StoredRuns);
        var today = metrics.MetricsToday!;
        Assert.Equal("2026-10-11", today.Day);
        Assert.Equal(18, today.Requests);
        Assert.Equal(523530L, today.Tokens);
        Assert.Equal(21258.96, today.LatencyP95Ms, precision: 2);
    }

    [Fact]
    public void AttentionFlagsSurfaceForDegradedPlatforms()
    {
        var health = JsonSerializer.Deserialize<DetailedHealth>(DetailedJson, Json)!;
        var degraded = health.Platforms["a2a"];

        Assert.True(degraded.NeedsAttention);
        Assert.Equal("degraded", degraded.State);
        Assert.Equal("timeout", degraded.ErrorCode);
        Assert.Equal("heartbeat missed", degraded.ErrorMessage);
        Assert.Null(degraded.Metrics);
    }

    [Fact]
    public void MissingReadinessAndMetricsTolerated()
    {
        const string json = """
            {"status": "ok", "platform": "hermes-agent", "version": "0.21.6",
             "gateway_state": "running", "platforms": {}}
            """;

        var health = JsonSerializer.Deserialize<DetailedHealth>(json, Json)!;

        Assert.Null(health.Readiness);
        Assert.Empty(health.Platforms);
    }
}
