using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Client.Core;

// /v1/runs wire shapes, verified live against gateway 0.21.6 (2026-10-11).
// There is NO list endpoint: GET /v1/runs answers 405. Clients track the run
// ids they create (or receive via toast deep links) and poll per run.

// POST /v1/runs -> 202 {"run_id", "status": "started", "replayed": false}.
public sealed record RunCreateResponse(
    string RunId,
    string Status,
    bool Replayed);

// GET /v1/runs/{id} -> 200. Terminal statuses: completed | failed | cancelled.
// Field set varies by status: usage/runtime/output arrive on completion,
// approval only while waiting_for_approval.
public sealed record RunStatus(
    string RunId,
    string Status,
    [property: JsonConverter(typeof(EpochDateTimeOffsetConverter))] DateTimeOffset? CreatedAt,
    [property: JsonConverter(typeof(EpochDateTimeOffsetConverter))] DateTimeOffset? UpdatedAt,
    string? SessionId,
    string? Model,
    string? LastEvent,
    string? Output,
    RunUsage? Usage,
    RunRuntime? Runtime,
    bool Completed = false,
    bool Partial = false,
    bool Interrupted = false,
    string? PendingSteer = null)
{
    public bool IsTerminal => Status is "completed" or "failed" or "cancelled";

    public bool IsWaitingForApproval => Status == "waiting_for_approval";
}

public sealed record RunUsage(
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    long CacheReadTokens = 0,
    long CacheWriteTokens = 0);

public sealed record RunRuntime(
    string? Provider,
    string? Model,
    string? RouteSource);

// One parsed run-lifecycle SSE frame from GET /v1/runs/{id}/events.
// The gateway puts the event name in data.event (there is no SSE event:
// line); frames open with ": open" and close with ": stream closed".
public sealed record RunEvent(
    string Name,
    string? RunId,
    long Seq,
    DateTimeOffset Timestamp,
    string? SessionId = null,
    string? Delta = null,        // message.delta
    string? Text = null,         // reasoning.available / message.interim
    string? Output = null,       // run.completed
    RunUsage? Usage = null,      // run.completed
    string? Choice = null,       // approval.responded
    int? Resolved = null,        // approval.responded
    bool? Accepted = null,       // run.steered
    string? Error = null,        // run.failed / error frames
    string? ToolName = null,     // approval.request
    string? Command = null,      // approval.request
    string? Description = null,  // approval.request
    string? MessageId = null,    // approval.request
    string? RequestId = null,    // approval.request
    IReadOnlyList<string>? Choices = null) // approval.request
{
    public bool IsTerminal => Name is "run.completed" or "run.failed" or "run.cancelled";

    public bool IsApprovalRequest => Name == "approval.request";

    /// <summary>Local-time clock string for timeline rows.</summary>
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>One-line human summary for timeline rows and the replay card.</summary>
    public string Summary => Name switch    {
        "message.delta" => Delta ?? string.Empty,
        "message.interim" => Text ?? string.Empty,
        "reasoning.available" => Text ?? string.Empty,
        "run.completed" => Output ?? "(completed)",
        "run.failed" => Error ?? "(failed)",
        "approval.request" => Command ?? Description ?? "(approval requested)",
        "approval.responded" => $"resolved: {Choice}",
        "run.steered" => "steer accepted",
        "run.stopping" => "stopping…",
        _ => Text ?? Delta ?? Output ?? string.Empty,
    };

    /// <summary>
    /// The approval card payload for <see cref="IsApprovalRequest"/> events.
    /// </summary>
    public ApprovalRequest ToApprovalRequest() => new(
        RunId ?? string.Empty,
        SessionId,
        MessageId,
        Command,
        ToolName,
        Description,
        Choices ?? [],
        RequestId);

    /// <summary>
    /// Maps a raw <see cref="SseEvent"/> to a retained, ownership-free record.
    /// The SSE event is NOT retained (it owns its JsonDocument); only
    /// primitives are copied out, so the caller may dispose it immediately.
    /// </summary>
    public static RunEvent FromSse(SseEvent e)
    {
        var data = e.Data;
        var isObject = data.ValueKind == JsonValueKind.Object;
        string? GetString(string name) =>
            isObject && data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
        double GetDouble(string name, double fallback) =>
            isObject && data.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : fallback;
        bool? GetBool(string name) =>
            isObject && data.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.GetBoolean() : null;
        int? GetInt(string name) =>
            isObject && data.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;

        RunUsage? usage = null;
        if (isObject && data.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            usage = new RunUsage(
                u.TryGetProperty("input_tokens", out var it) && it.TryGetInt64(out var iv) ? iv : 0,
                u.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt64(out var ov) ? ov : 0,
                u.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt64(out var tv) ? tv : 0,
                u.TryGetProperty("cache_read_tokens", out var cr) && cr.TryGetInt64(out var crv) ? crv : 0,
                u.TryGetProperty("cache_write_tokens", out var cw) && cw.TryGetInt64(out var cwv) ? cwv : 0);
        }

        List<string>? choices = null;
        if (isObject && data.TryGetProperty("choices", out var c) && c.ValueKind == JsonValueKind.Array)
        {
            choices = [];
            foreach (var item in c.EnumerateArray())
            {
                if (item.GetString() is { } s)
                {
                    choices.Add(s);
                }
            }
        }

        return new RunEvent(
            GetString("event") ?? e.Name,
            e.RunId,
            e.Seq,
            DateTimeOffset.FromUnixTimeMilliseconds((long)(GetDouble("timestamp", e.Ts) * 1000)),
            e.SessionId,
            GetString("delta"),
            GetString("text"),
            GetString("output"),
            usage,
            GetString("choice"),
            GetInt("resolved"),
            GetBool("accepted"),
            GetString("error"),
            GetString("tool_name"),
            GetString("command"),
            GetString("description") ?? GetString("reason"),
            GetString("message_id"),
            GetString("request_id"),
            choices);
    }
}

// POST /v1/runs/{id}/steer {"message"|"input"|"text": ...} -> 200.
public sealed record SteerResponse(
    string RunId,
    bool Accepted);
