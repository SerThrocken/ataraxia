// Ops-surface DTOs: GET /health/detailed (verified live 2026-10-11,
// gateway 0.21.6). Platforms arrive as a NAME-KEYED OBJECT, not a list.
// CPU, memory, uptime, and restart history are NOT exposed by the gateway:
// the key scan of the full response found none of them (2026-10-11), so
// this file models only what the endpoint actually returns.
namespace Hermes.Client.Core;

/// <summary>
/// GET /health/detailed: gateway status, readiness checks, and the
/// per-platform connection table. The thin client reads ops state through
/// this endpoint — there is no ops/doctor, ops/backup, or update API on
/// the 8642 surface (verified: all 404; those routes live in the Acer's
/// serve backend only).
/// </summary>
public sealed record DetailedHealth(
    string Status,
    ReadinessChecks? Readiness,
    string? Platform,
    string? Version,
    string? GatewayState)
{
    /// <summary>Name-keyed platform table (e.g. "api_server", "coding-agent:a2a").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("platforms")]
    public Dictionary<string, PlatformStatus> Platforms { get; init; } = new();
}

public sealed record ReadinessChecks(
    string Status,
    CheckSet Checks);

/// <summary>
/// The per-check shapes inside readiness.checks. Members are nullable:
/// the gateway may add or drop checks across versions.
/// </summary>
public sealed record CheckSet(
    CheckStatus? StateDb,
    CheckStatus? SessionStore,
    CheckStatus? Config,
    CheckStatus? Model,
    DiskCheck? Disk,
    GatewayCheck? Gateway,
    BackgroundQueuesCheck? BackgroundQueues);

public record CheckStatus(string Status);

public sealed record DiskCheck(
    string Status,
    double UsedPercent,
    long FreeBytes) : CheckStatus(Status);

public sealed record GatewayCheck(
    string Status,
    int ConnectedPlatforms,
    int PlatformCount,
    string? State) : CheckStatus(Status);

public sealed record BackgroundQueuesCheck(
    string Status,
    int ActiveApiRuns,
    int ProcessCompletions,
    int ActiveDelegations) : CheckStatus(Status);

/// <summary>
/// One entry of the platforms table: connection state, last error, and
/// writer bookkeeping. The api_server entry additionally carries listener
/// info and request metrics.
/// </summary>
public sealed record PlatformStatus(
    string State,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? UpdatedAt,
    long WriterPid = 0,
    long WriterStartTime = 0,
    bool NeedsAttention = false,
    string? RetryingSince = null,
    string? ListenerBase = null,
    ApiServerMetrics? Metrics = null);

public sealed record ApiServerMetrics(
    string? Host,
    int Port,
    int ActiveRuns,
    int StoredRuns,
    DateTimeOffset? LastRequestAt,
    DateTimeOffset? LastHeartbeat,
    MetricsToday? MetricsToday);

public sealed record MetricsToday(
    string? Day,
    int Requests,
    int Messages,
    long Tokens,
    double LatencyP95Ms);
