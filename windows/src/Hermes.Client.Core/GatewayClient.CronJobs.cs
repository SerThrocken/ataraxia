using System.Net.Http.Json;
using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// Cron-job surface of the gateway client: GET/POST /api/jobs,
/// GET/PATCH/DELETE /api/jobs/{id}, POST /api/jobs/{id}/{pause,resume,run}.
/// Implemented on the concrete class so every consumer shares the plumbing
/// in <see cref="GatewayClientBase"/> (same pattern as GatewayClient.Ops.cs).
/// </summary>
public sealed partial class GatewayClient
{
    /// <summary>
    /// GET /api/jobs. Pass <c>includeDisabled</c> to also list paused jobs.
    /// Throws <see cref="GatewayApiException"/> 501 when the gateway's cron
    /// module is unavailable.
    /// </summary>
    public override async Task<CronJobList> GetCronJobsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var uri = includeDisabled ? "api/jobs?include_disabled=true" : "api/jobs";
        var list = await GetJsonAsync<CronJobList>(uri, cancellationToken).ConfigureAwait(false);
        return list ?? throw new GatewayApiException(200, "Empty cron jobs response.");
    }

    /// <summary>GET /api/jobs/{job_id} — the gateway 400s malformed ids, 404s unknown ones.</summary>
    public override async Task<CronJob> GetCronJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        var response = await GetJsonAsync<CronJobResponse>(
                $"api/jobs/{Uri.EscapeDataString(jobId)}", cancellationToken).ConfigureAwait(false);
        return response?.Job ?? throw new GatewayApiException(200, "Empty cron job response.");
    }

    /// <summary>POST /api/jobs {"name", "schedule", "prompt", ...} — the gateway 400s bad bodies.</summary>
    public override async Task<CronJob> CreateCronJobAsync(CronJobCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await Http.PostAsync(
                "api/jobs",
                JsonContent.Create(request, options: Json),
                cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<CronJobResponse>(json, Json)?.Job
            ?? throw new GatewayApiException(200, "Empty cron job create response.");
    }

    /// <summary>PATCH /api/jobs/{job_id} — only the set fields of <paramref name="update"/> are sent.</summary>
    public override async Task<CronJob> UpdateCronJobAsync(string jobId, CronJobUpdateRequest update, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(update);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"api/jobs/{Uri.EscapeDataString(jobId)}")
        {
            Content = JsonContent.Create(update, options: Json),
        };
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<CronJobResponse>(json, Json)?.Job
            ?? throw new GatewayApiException(200, "Empty cron job update response.");
    }

    /// <summary>DELETE /api/jobs/{job_id} — true when the gateway answers {"ok": true}.</summary>
    public override async Task<bool> DeleteCronJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        using var response = await Http.DeleteAsync(
                $"api/jobs/{Uri.EscapeDataString(jobId)}", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<CronJobDeleteResponse>(json, Json)?.Ok == true;
    }

    /// <summary>POST /api/jobs/{job_id}/pause.</summary>
    public override Task<CronJob> PauseCronJobAsync(string jobId, CancellationToken cancellationToken = default) =>
        PostCronJobActionAsync(jobId, "pause", null, cancellationToken);

    /// <summary>POST /api/jobs/{job_id}/resume.</summary>
    public override Task<CronJob> ResumeCronJobAsync(string jobId, CancellationToken cancellationToken = default) =>
        PostCronJobActionAsync(jobId, "resume", null, cancellationToken);

    /// <summary>
    /// POST /api/jobs/{job_id}/run — triggers immediate execution of the job.
    /// The optional <paramref name="extraPrompt"/> rides in the body as
    /// <c>{"prompt": ...}</c>. The gateway answers 404 only for an unknown
    /// job id (the route itself always exists); 400 for a malformed id.
    /// </summary>
    public override Task<CronJob> RunCronJobAsync(string jobId, string? extraPrompt = null, CancellationToken cancellationToken = default) =>
        PostCronJobActionAsync(jobId, "run", extraPrompt is null ? null : new { prompt = extraPrompt }, cancellationToken);

    private async Task<CronJob> PostCronJobActionAsync(
        string jobId, string action, object? body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        using var response = await Http.PostAsync(
                $"api/jobs/{Uri.EscapeDataString(jobId)}/{action}",
                JsonContent.Create(body ?? new { }, options: Json),
                cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<CronJobResponse>(json, Json)?.Job
            ?? throw new GatewayApiException(200, $"Empty cron job {action} response.");
    }
}
