using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// Concrete gateway client. The chat surface (paged sessions, typed SSE stream,
/// approval resolution, artifact download) is inherited concrete from
/// <see cref="GatewayClientBase"/>; the legacy abstract members are implemented
/// here in terms of the same shared plumbing. No HTTP/auth code is duplicated.
/// </summary>
public sealed class GatewayClient : GatewayClientBase
{
    public GatewayClient(HttpClient httpClient, GatewayOptions options)
        : base(httpClient, options)
    {
    }

    public override async Task<IReadOnlyList<Session>> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        (await GetSessionsPageAsync(50, 0, cancellationToken).ConfigureAwait(false)).Sessions;

    public override async Task<Session> CreateSessionAsync(string? title = null, CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsync(
                "api/sessions",
                JsonContent.Create(new { title }, options: Json),
                cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<SessionCreateResponse>(json, Json)?.Session
            ?? throw new GatewayApiException(200, "Empty session create response.");
    }

    public override async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var list = await GetJsonAsync<MessageList>(
                $"api/sessions/{Uri.EscapeDataString(sessionId)}/messages", cancellationToken)
            .ConfigureAwait(false);
        return list?.Messages ?? [];
    }

    public override async Task<ChatMessage> SendChatAsync(string sessionId, string content, CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        string? messageId = null;
        await foreach (var e in StreamChatEventsAsync(sessionId, content, cancellationToken).ConfigureAwait(false))
        {
            using (e)
            {
                if (e.Name == "assistant.delta" && e.GetString("delta") is { } delta)
                {
                    messageId ??= e.GetString("message_id");
                    text.Append(delta);
                }
            }
        }

        return new ChatMessage(messageId, sessionId, "assistant", text.ToString(), null, null, null, null, null, null);
    }

    public override async IAsyncEnumerable<string> StreamChatAsync(
        string sessionId, string content, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var e in StreamChatEventsAsync(sessionId, content, cancellationToken).ConfigureAwait(false))
        {
            using (e)
            {
                if (e.Name == "assistant.delta" && e.GetString("delta") is { } delta)
                {
                    yield return delta;
                }
            }
        }
    }

    public override async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetJsonAsync<ModelList>("v1/models", cancellationToken).ConfigureAwait(false);
        return list?.Data.Select(m => new ModelInfo(m.Id, m.Id, null)).ToList() ?? [];
    }

    public override async Task<IReadOnlyList<SkillInfo>> GetSkillsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetJsonAsync<SkillList>("v1/skills", cancellationToken).ConfigureAwait(false);
        return list?.Data.Select(s => new SkillInfo(s.Name, s.Name, s.Description)).ToList() ?? [];
    }

    public override async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // Boolean probe: any failure (network, non-200) is an unhealthy answer,
        // not an exception the caller must handle.
        try
        {
            using var response = await Http.GetAsync("health", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
