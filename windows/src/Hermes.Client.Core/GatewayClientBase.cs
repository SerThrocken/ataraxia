using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Client.Core;

/// <summary>
/// Shared HttpClient + System.Text.Json plumbing for <see cref="IGatewayClient"/>
/// implementations. The gateway speaks snake_case; the shared options translate.
/// Chat-surface members are concrete here so every implementation shares them;
/// the legacy abstract members are implemented by <see cref="GatewayClient"/>.
/// </summary>
public abstract partial class GatewayClientBase : IGatewayClient, IGatewayEndpoint
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
    public abstract Task<IReadOnlyList<ToolsetInfo>> GetToolsetsAsync(CancellationToken cancellationToken = default);
    public abstract Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /api/sessions/{id}. Response envelope is <c>{"object": "hermes.session",
    /// "session": {...}}</c> (verified against gateway source, 2026-10-11) —
    /// the same envelope POST /api/sessions returns, so <see cref="SessionCreateResponse"/>
    /// is reused. Null when the gateway answers 200 with an empty body.
    /// </summary>
    public virtual async Task<Session?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using var response = await Http.GetAsync(
                $"api/sessions/{Uri.EscapeDataString(sessionId)}", cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<SessionCreateResponse>(json, Json)?.Session;
    }

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
    /// Serializes the chat-stream request body. Text-only turns keep the exact
    /// legacy wire shape (<c>{"message":"..."}</c>); a photo switches the body
    /// to the OpenAI-style parts array the gateway's
    /// <c>_normalize_multimodal_content</c> accepts
    /// (verified against gateway source, 2026-10-11): text + image_url parts
    /// with a <c>data:image/...;base64,...</c> URL.
    /// </summary>
    public static string BuildChatMessageJson(string text, ChatImageAttachment? image)
    {
        object body = image is null
            ? new { message = text }
            : new
            {
                message = new object[]
                {
                    new { type = "text", text },
                    new { type = "image_url", image_url = new { url = image.ToDataUrl() } },
                },
            };
        return JsonSerializer.Serialize(body, Json);
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
            Content = new StringContent(BuildChatMessageJson(message, image: null), Encoding.UTF8, "application/json"),
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

    /// <summary>
    /// Chat-stream turn carrying a photo (parts-array body). Shares the event
    /// parsing with <see cref="StreamChatEventsAsync"/>.
    /// </summary>
    public virtual async IAsyncEnumerable<SseEvent> StreamChatEventsWithImageAsync(
        string sessionId,
        string text,
        ChatImageAttachment image,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(image);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/sessions/{Uri.EscapeDataString(sessionId)}/chat/stream")
        {
            Content = new StringContent(BuildChatMessageJson(text, image), Encoding.UTF8, "application/json"),
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

    /// <summary>
    /// Non-streaming chat turn with a photo: drains the SSE stream into one
    /// <see cref="ChatMessage"/>, mirroring the text-only implementations.
    /// </summary>
    public virtual async Task<ChatMessage> SendChatWithImageAsync(
        string sessionId, string text, ChatImageAttachment image, CancellationToken cancellationToken = default)
    {
        var reply = new StringBuilder();
        string? messageId = null;
        await foreach (var e in StreamChatEventsWithImageAsync(sessionId, text, image, cancellationToken).ConfigureAwait(false))
        {
            using (e)
            {
                if (e.Name == "assistant.delta" && e.GetString("delta") is { } delta)
                {
                    messageId ??= e.GetString("message_id");
                    reply.Append(delta);
                }
            }
        }

        return new ChatMessage(messageId, sessionId, "assistant", reply.ToString(), null, null, null, null, null, null);
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
    public virtual Task<ArtifactDownload> DownloadArtifactAsync(
        string artifactId, CancellationToken cancellationToken = default) =>
        DownloadArtifactWithProgressAsync(artifactId, progress: null, cancellationToken);

    /// <summary>
    /// GET /v1/artifacts/download/{id} streamed with byte progress. Reports
    /// <see cref="ArtifactDownloadProgress"/> as chunks arrive;
    /// <c>TotalBytes</c> is null when the gateway omits Content-Length.
    /// </summary>
    public virtual async Task<ArtifactDownload> DownloadArtifactWithProgressAsync(
        string artifactId, IProgress<ArtifactDownloadProgress>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        using var response = await Http.GetAsync(
                $"v1/artifacts/download/{Uri.EscapeDataString(artifactId)}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var total = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream((int)Math.Min(total ?? 0, 16 * 1024 * 1024));
        var chunk = new byte[8192];
        long received = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new ArtifactDownloadProgress(received, total));
        }

        var data = buffer.ToArray();
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
