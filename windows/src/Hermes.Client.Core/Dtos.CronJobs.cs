// Cron-job DTOs: the /api/jobs family on the 8642 gateway.
// Record shape verified against the gateway source (cron/jobs.py create_job,
// 2026-10-11) and the live GET /api/jobs -> {"jobs": [...]} response.
// Timestamps arrive as ISO-8601 strings (not epoch floats like sessions).
// The /api/cron/jobs routes in hermes_cli/cron.py are serve-backend-only
// (404 on 8642) — this file models only the /api/jobs surface.
namespace Hermes.Client.Core;

public sealed record CronJobList(IReadOnlyList<CronJob> Jobs);

public sealed record CronJobResponse(CronJob Job);

public sealed record CronJobDeleteResponse(bool Ok);

/// <summary>
/// A stored cron job. Job ids are 12 lowercase hex chars; the gateway 400s
/// anything else. <c>Repeat.Times</c> null means "forever".
/// </summary>
public sealed record CronJob(
    string Id,
    string Name,
    string? Prompt,
    IReadOnlyList<string> Skills,
    string? Skill,
    string? Model,
    string? Provider,
    string? ScheduleDisplay,
    CronSchedule? Schedule,
    CronRepeat? Repeat,
    bool Enabled,
    string? State,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastStatus,
    string? LastError,
    CronFailure? LastFailure,
    int FailureStreak,
    string? Deliver,
    string? PausedReason,
    DateTimeOffset? PausedAt,
    CronJobOrigin? Origin);

/// <summary>Parsed schedule: kind is "once", "interval", or "cron".</summary>
public sealed record CronSchedule(string? Kind, string? Expr, string? Display);

/// <summary>Where the job was created (gateway-verified 2026-10-11).</summary>
public sealed record CronJobOrigin(
    string? Platform,
    string? ChatId,
    string? SourceIp,
    string? PeerIp,
    string? UserAgent);

public sealed record CronRepeat(int? Times, int Completed);

/// <summary>Most recent failed run ({at, detail}); not cleared by later success.</summary>
public sealed record CronFailure(DateTimeOffset? At, string? Detail);

/// <summary>
/// POST /api/jobs body. Name and schedule are required gateway-side;
/// deliver defaults to "local"; paused creates the job without arming it.
/// </summary>
public sealed record CronJobCreateRequest(
    string Name,
    string Schedule,
    string? Prompt = null,
    IReadOnlyList<string>? Skills = null,
    int? Repeat = null,
    string? Deliver = null,
    bool Paused = false);

/// <summary>
/// PATCH /api/jobs/{id} body: the gateway whitelists name, schedule, prompt,
/// deliver, skills, skill, repeat, enabled — only set fields are sent.
/// </summary>
public sealed record CronJobUpdateRequest(
    string? Name = null,
    string? Schedule = null,
    string? Prompt = null,
    string? Deliver = null,
    IReadOnlyList<string>? Skills = null,
    int? Repeat = null,
    bool? Enabled = null);
