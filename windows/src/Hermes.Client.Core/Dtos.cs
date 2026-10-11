using System.Text.Json.Serialization;

namespace Hermes.Client.Core;

// Wire shapes verified against the gateway contract (api-contract.md, 2026-10-11).
// Timestamps arrive as epoch-second floats; content may be a string or parts array.

public sealed record Session(
    string Id,
    string? Title,
    string? Preview,
    int MessageCount,
    string? Model,
    [property: JsonConverter(typeof(EpochDateTimeOffsetConverter))] DateTimeOffset? LastActive,
    [property: JsonConverter(typeof(EpochDateTimeOffsetConverter))] DateTimeOffset? StartedAt,
    [property: JsonConverter(typeof(EpochDateTimeOffsetConverter))] DateTimeOffset? EndedAt,
    bool Pinned,
    bool Archived);

public sealed record SessionPage(
    [property: JsonPropertyName("data")] IReadOnlyList<Session> Sessions,
    int Limit,
    int Offset,
    bool HasMore);

public sealed record ChatMessage(
    [property: JsonConverter(typeof(FlexibleStringConverter))] string? Id,
    string? SessionId,
    string Role,
    [property: JsonConverter(typeof(MessageContentConverter))] string Content,
    [property: JsonConverter(typeof(EpochDateTimeOffsetConverter))][property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp,
    string? FinishReason,
    string? Reasoning,
    string? ReasoningContent,
    string? ToolName,
    string? DisplayKind);

public sealed record MessageList(
    string? SessionId,
    [property: JsonPropertyName("data")] IReadOnlyList<ChatMessage> Messages);

public sealed record SessionCreateResponse(Session Session);

public sealed record ModelInfo(
    string Id,
    string Name,
    string? Provider);

public sealed record SkillInfo(
    string Id,
    string Name,
    string? Description);

internal sealed record ModelList(IReadOnlyList<ModelDto> Data);

internal sealed record ModelDto(string Id);

internal sealed record SkillList(IReadOnlyList<SkillDto> Data);

internal sealed record SkillDto(string Name, string? Description);

/// <summary>
/// A pending dangerous-command approval from an <c>approval.request</c> SSE event.
/// Resolve via <c>POST /v1/runs/{run_id}/approval</c> keyed by <see cref="RunId"/>.
/// </summary>
public sealed record ApprovalRequest(
    string RunId,
    string? SessionId,
    string? MessageId,
    string? Command,
    string? ToolName,
    string? Description,
    IReadOnlyList<string> Choices,
    string? RequestId)
{
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public static ApprovalRequest FromEvent(SseEvent e)
    {
        var choices = new List<string>();
        if (e.Data.TryGetProperty("choices", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in c.EnumerateArray())
            {
                if (item.GetString() is { } s)
                {
                    choices.Add(s);
                }
            }
        }

        return new ApprovalRequest(
            e.RunId ?? "",
            e.SessionId,
            e.GetString("message_id"),
            e.GetString("command"),
            e.GetString("tool_name"),
            e.GetString("description") ?? e.GetString("reason"),
            choices,
            e.GetString("request_id"));
    }
}

public sealed record ApprovalResolution(
    string RunId,
    string Choice,
    int Resolved,
    string? RequestId);

public sealed record ArtifactDownload(
    string ArtifactId,
    string? FileName,
    string? ContentType,
    string? Sha256,
    byte[] Data);
