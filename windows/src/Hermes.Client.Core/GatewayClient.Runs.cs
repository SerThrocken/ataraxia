using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// The /v1/runs agent-run surface: create, poll, stream events, steer,
/// stop, and session forking. Shapes verified live against gateway 0.21.6
/// (2026-10-11); see <c>RUNS-API.md</c> at the repo root.
/// </summary>
public sealed partial class GatewayClient
{
    public override async Task<RunCreateResponse> CreateRunAsync(
        string input,
        string? sessionId = null,
        string? model = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var body = new Dictionary<string, string?> { ["input"] = input };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            body["session_id"] = sessionId;
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            body["model"] = model;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/runs")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<RunCreateResponse>(json, Json)
            ?? throw new GatewayApiException(200, "Empty run create response.");
    }

    public override async Task<RunStatus> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var status = await GetJsonAsync<RunStatus>(
                $"v1/runs/{Uri.EscapeDataString(runId)}", cancellationToken).ConfigureAwait(false);
        return status ?? throw new GatewayApiException(200, "Empty run status response.");
    }

    public override async IAsyncEnumerable<RunEvent> StreamRunEventsAsync(
        string runId,
        long? lastSeq = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var uri = $"v1/runs/{Uri.EscapeDataString(runId)}/events"
            + (lastSeq.HasValue ? $"?last_seq={lastSeq.Value}" : string.Empty);
        using var response = await Http.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var e in SseParser.ReadEventsAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            using (e)
            {
                yield return RunEvent.FromSse(e);
            }
        }
    }

    public override async Task<SteerResponse> SteerRunAsync(
        string runId, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        using var response = await Http.PostAsync(
                $"v1/runs/{Uri.EscapeDataString(runId)}/steer",
                JsonContent.Create(new { message }, options: Json),
                cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SteerResponse>(json, Json)
            ?? throw new GatewayApiException(200, "Empty steer response.");
    }

    public override async Task<RunStatus> StopRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        using var response = await Http.PostAsync(
                $"v1/runs/{Uri.EscapeDataString(runId)}/stop",
                JsonContent.Create(new { }, options: Json),
                cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<RunStatus>(json, Json)
            ?? throw new GatewayApiException(200, "Empty stop response.");
    }

    public override async Task<Session> ForkSessionAsync(
        string sessionId, string? title = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var response = await Http.PostAsync(
                $"api/sessions/{Uri.EscapeDataString(sessionId)}/fork",
                JsonContent.Create(new { title }, options: Json),
                cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SessionCreateResponse>(json, Json)?.Session
            ?? throw new GatewayApiException(200, "Empty session fork response.");
    }
}
