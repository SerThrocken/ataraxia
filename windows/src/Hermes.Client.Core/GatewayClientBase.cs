using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Client.Core;

/// <summary>
/// Shared HttpClient + System.Text.Json plumbing for <see cref="IGatewayClient"/>
/// implementations. The gateway speaks snake_case; the shared options translate.
/// Chat-surface members are concrete here so every implementation shares them;
/// the legacy abstract members are implemented by <see cref="GatewayClient"/>.
/// </summary>
public abstract class GatewayClientBase : IGatewayClient
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    protected readonly HttpClient Http;

    protected GatewayClientBase(HttpClient httpClient, GatewayOptions options)
    {
        Http = httpClient;
        if (options.BaseUrl is not null)
        {
            Http.BaseAddress = options.BaseUrl;
        }

        SetApiKey(options.ApiKey);
    }

    /// <summary>
    /// Sets (or clears) the Bearer API key. Called by the connection flow once
    /// the key is loaded from the secret store.
    /// </summary>
    public void SetApiKey(string? apiKey)
    {
        Http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Points the client at a different gateway base URL (e.g. after the
    /// user changes it in Settings). Called by the connection flow.
    /// </summary>
    public void SetBaseUrl(Uri baseUrl)
    {
        Http.BaseAddress = baseUrl;
    }

    public abstract Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default);
    public abstract Task<Session> CreateSessionAsync(string? title = null, CancellationToken cancellationToken = default);
    public abstract Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default);
    public abstract Task<ChatMessage> SendChatAsync(string sessionId, string content, CancellationToken cancellationToken = default);
    public abstract IAsyncEnumerable<string> StreamChatAsync(string sessionId, string content, CancellationToken cancellationToken = default);
    public abstract Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);
    public abstract Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default);
    public abstract Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>GET /api/sessions with limit/offset pagination.</summary>
    public virtual async Task<SessionPage> GetSessionsPageAsync(int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return await GetJsonAsync<SessionPage>(
                $"api/sessions?limit={limit}&offset={offset}", cancellationToken).ConfigureAwait(false)
            ?? throw new GatewayApiException(200, "Empty session list response.");
    }

    /// <summary>
    /// POST /api/sessions/{id}/chat/stream; yields typed SSE events
    /// (assistant.delta, tool.progress, approval.request, …, done).
    /// </summary>
    public virtual async IAsyncEnumerable<SseEvent> StreamChatEventsAsync(
        string sessionId,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/sessions/{Uri.EscapeDataString(sessionId)}/chat/stream")
        {
            Content = JsonContent.Create(new { message }, options: Json),
        };
        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var e in SseParser.ReadEventsAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            yield return e;
        }
    }

    /// <summary>POST /v1/runs/{run_id}/approval {"choice": …}.</summary>
    public virtual async Task<ApprovalResolution> ResolveApprovalAsync(
        string runId, string choice, string? requestId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(choice);
        var body = new Dictionary<string, string> { ["choice"] = choice };
        if (!string.IsNullOrEmpty(requestId))
        {
            body["request_id"] = requestId;
        }

        using var response = await Http.PostAsync(
                $"v1/runs/{Uri.EscapeDataString(runId)}/approval",
                JsonContent.Create(body, options: Json),
                cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ApprovalResolution>(json, Json)
            ?? throw new GatewayApiException(200, "Empty approval response.");
    }

    /// <summary>GET /v1/artifacts/download/{id}: one-shot raw bytes + receipt headers.</summary>
    public virtual async Task<ArtifactDownload> DownloadArtifactAsync(
        string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        using var response = await Http
            .GetAsync($"v1/artifacts/download/{Uri.EscapeDataString(artifactId)}", cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = disposition?.FileNameStar ?? disposition?.FileName;
        response.Headers.TryGetValues("X-Artifact-Sha256", out var sha256);
        response.Headers.TryGetValues("X-Artifact-Id", out var id);

        return new ArtifactDownload(
            id?.FirstOrDefault() ?? artifactId,
            fileName?.Trim('"'),
            response.Content.Headers.ContentType?.ToString(),
            sha256?.FirstOrDefault(),
            data);
    }

    protected async Task<T?> GetJsonAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(relativeUri, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(json, Json);
    }

    protected static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw GatewayApiException.FromResponse((int)response.StatusCode, body);
    }

    public virtual ValueTask DisposeAsync()
    {
        Http.Dispose();
        return ValueTask.CompletedTask;
    }
}
