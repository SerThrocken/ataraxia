namespace Hermes.Client.Core;

/// <summary>
/// Cron-job surface on the abstract base: concrete clients implement these
/// with <c>override</c>, following the chat-surface pattern.
/// </summary>
public abstract partial class GatewayClientBase
{
    /// <summary>GET /api/jobs[?include_disabled=true].</summary>
    public abstract Task<CronJobList> GetCronJobsAsync(
        bool includeDisabled = false, CancellationToken cancellationToken = default);

    /// <summary>GET /api/jobs/{job_id}.</summary>
    public abstract Task<CronJob> GetCronJobAsync(
        string jobId, CancellationToken cancellationToken = default);

    /// <summary>POST /api/jobs.</summary>
    public abstract Task<CronJob> CreateCronJobAsync(
        CronJobCreateRequest request, CancellationToken cancellationToken = default);

    /// <summary>PATCH /api/jobs/{job_id}.</summary>
    public abstract Task<CronJob> UpdateCronJobAsync(
        string jobId, CronJobUpdateRequest update, CancellationToken cancellationToken = default);

    /// <summary>DELETE /api/jobs/{job_id}.</summary>
    public abstract Task<bool> DeleteCronJobAsync(
        string jobId, CancellationToken cancellationToken = default);

    /// <summary>POST /api/jobs/{job_id}/pause.</summary>
    public abstract Task<CronJob> PauseCronJobAsync(
        string jobId, CancellationToken cancellationToken = default);

    /// <summary>POST /api/jobs/{job_id}/resume.</summary>
    public abstract Task<CronJob> ResumeCronJobAsync(
        string jobId, CancellationToken cancellationToken = default);

    /// <summary>POST /api/jobs/{job_id}/run.</summary>
    public abstract Task<CronJob> RunCronJobAsync(
        string jobId, string? extraPrompt = null, CancellationToken cancellationToken = default);
}
