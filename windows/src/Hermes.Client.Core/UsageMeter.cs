using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// One completed turn's token usage, parsed from the SSE <c>run.completed</c>
/// event's <c>usage</c> payload (api-contract.md §6). Cost is never derived
/// here — it comes from the gateway's <c>estimated_cost_usd</c> on the session.
/// </summary>
public sealed record TurnUsage(int TurnNumber, long InputTokens, long OutputTokens, DateTimeOffset RecordedAt)
{
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>Per-turn usage line for the breakdown flyout.</summary>
    public string Summary =>
        $"Turn {TurnNumber}: {InputTokens:N0} in / {OutputTokens:N0} out";

    /// <summary>
    /// Extracts usage from a <c>run.completed</c> event; null for any other
    /// event or when the payload carries no usable <c>usage</c> object.
    /// </summary>
    public static TurnUsage? FromEvent(SseEvent e, int turnNumber)
    {
        if (e.Name != "run.completed"
            || e.Data.ValueKind != JsonValueKind.Object
            || !e.Data.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object
            || !TryGetInt64(usage, "input_tokens", out var input)
            || !TryGetInt64(usage, "output_tokens", out var output))
        {
            return null;
        }

        return new TurnUsage(turnNumber, input, output, DateTimeOffset.UtcNow);
    }

    private static bool TryGetInt64(JsonElement obj, string name, out long value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetInt64(out value);
    }
}

/// <summary>
/// Session-scoped token/cost meter. Seeded from the session resource when a
/// chat opens, accumulated per turn from SSE <c>run.completed</c> usage, and
/// re-anchored to the gateway's authoritative session totals + cost after
/// each turn. Mutations are expected on the UI thread.
/// </summary>
public sealed class UsageMeter : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string? SessionId { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public long TotalTokens => InputTokens + OutputTokens;
    public double? EstimatedCostUsd { get; private set; }

    /// <summary>
    /// Status-bar text: "12,345 tokens · $0.0234", or just the token count when
    /// the gateway has not reported a cost. (A formatted property instead of a
    /// XAML converter: x:Bind converters don't compile on a WinUI 3 Window.)
    /// </summary>
    public string SummaryText =>
        EstimatedCostUsd is { } cost
            ? $"{TotalTokens:N0} tokens · ${cost:F4}"
            : $"{TotalTokens:N0} tokens";

    /// <summary>Screen-reader name for the meter button; updates with the values.</summary>
    public string ScreenReaderText =>
        EstimatedCostUsd is { } cost
            ? $"Token usage: {TotalTokens:N0} tokens, {InputTokens:N0} input, " +
              $"{OutputTokens:N0} output, estimated cost ${cost:F4}. Activate for per-turn breakdown."
            : $"Token usage: {TotalTokens:N0} tokens, {InputTokens:N0} input, " +
              $"{OutputTokens:N0} output. Activate for per-turn breakdown.";

    public ObservableCollection<TurnUsage> Turns { get; } = new();

    /// <summary>Starts (or restarts) metering for a session, seeded from its stored totals.</summary>
    public void BeginSession(string sessionId, long inputTokens, long outputTokens, double? estimatedCostUsd)
    {
        Turns.Clear();
        SessionId = sessionId;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        EstimatedCostUsd = estimatedCostUsd;
        NotifyAll();
    }

    /// <summary>Records one completed turn's usage, parsed from its SSE event.</summary>
    public void RecordTurn(TurnUsage turn)
    {
        Turns.Add(turn);
        InputTokens += turn.InputTokens;
        OutputTokens += turn.OutputTokens;
        Notify(nameof(InputTokens));
        Notify(nameof(OutputTokens));
        Notify(nameof(TotalTokens));
        Notify(nameof(SummaryText));
        Notify(nameof(ScreenReaderText));
    }

    /// <summary>
    /// Adopts the gateway's authoritative session totals and cost (assignment,
    /// not addition — the recorded turn is already included server-side).
    /// Ignored when the session id doesn't match the metered session.
    /// </summary>
    public void SyncFromSession(Session session)
    {
        if (session.Id != SessionId)
        {
            return;
        }

        InputTokens = session.InputTokens;
        OutputTokens = session.OutputTokens;
        EstimatedCostUsd = session.EstimatedCostUsd;
        NotifyAll();
    }

    private void NotifyAll()
    {
        Notify(nameof(SessionId));
        Notify(nameof(InputTokens));
        Notify(nameof(OutputTokens));
        Notify(nameof(TotalTokens));
        Notify(nameof(EstimatedCostUsd));
        Notify(nameof(SummaryText));
        Notify(nameof(ScreenReaderText));
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
