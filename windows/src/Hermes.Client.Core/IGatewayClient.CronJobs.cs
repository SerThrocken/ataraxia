namespace Hermes.Client.Core;

/// <summary>
/// Cron-job surface of the gateway client: <c>GET/POST /api/jobs</c>,
/// <c>GET/PATCH/DELETE /api/jobs/{job_id}</c>, and
/// <c>POST /api/jobs/{job_id}/{pause,resume,run}</c>.
/// Verified live against the 8642 gateway, 2026-10-11. The gateway answers
/// 501 when its cron module is unavailable — surfaced as
/// <see cref="GatewayApiException"/> like every other gateway error.
/// </summary>
public partial interface IGatewayClient
{
    // GET /api/jobs[?include_disabled=true]
    Task<CronJobList> GetCronJobsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);

    // GET /api/jobs/{job_id}
    Task<CronJob> GetCronJobAsync(string jobId, CancellationToken cancellationToken = default);

    // POST /api/jobs
    Task<CronJob> CreateCronJobAsync(CronJobCreateRequest request, CancellationToken cancellationToken = default);

    // PATCH /api/jobs/{job_id}
    Task<CronJob> UpdateCronJobAsync(string jobId, CronJobUpdateRequest update, CancellationToken cancellationToken = default);

    // DELETE /api/jobs/{job_id} -> {"ok": true}
    Task<bool> DeleteCronJobAsync(string jobId, CancellationToken cancellationToken = default);

    // POST /api/jobs/{job_id}/pause
    Task<CronJob> PauseCronJobAsync(string jobId, CancellationToken cancellationToken = default);

    // POST /api/jobs/{job_id}/resume
    Task<CronJob> ResumeCronJobAsync(string jobId, CancellationToken cancellationToken = default);

    // POST /api/jobs/{job_id}/run (triggers immediate execution; optional extra prompt)
    Task<CronJob> RunCronJobAsync(string jobId, string? extraPrompt = null, CancellationToken cancellationToken = default);
}
