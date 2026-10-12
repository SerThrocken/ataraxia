namespace Hermes.Client.Core;

/// <summary>Gateway reachability as the HUD shows it.</summary>
public enum HudConnectionState
{
    Unknown,
    Connected,
    Disconnected,
}

/// <summary>
/// The HUD's status-dot color, mapped to theme brushes by the view layer
/// (system resources only, so high-contrast themes track automatically).
/// </summary>
public enum HudDotKind
{
    Unknown,
    Ok,
    Working,
    Error,
}

/// <summary>One tracked agent run, as shown on the HUD's run line.</summary>
public sealed record HudRunSummary(string RunId, string Status, bool IsActive, string? LastEvent)
{
    public string ShortId => RunId.Length > 12 ? RunId[..12] + "…" : RunId;

    public string DisplayText => string.IsNullOrWhiteSpace(LastEvent)
        ? $"{ShortId} · {Status}"
        : $"{ShortId} · {Status} — {LastEvent}";
}

/// <summary>
/// The HUD's entire visible state in one immutable, UI-agnostic record.
/// The monitor publishes these; the view layer renders them.
/// </summary>
public sealed record HudSnapshot(
    HudConnectionState Connection,
    IReadOnlyList<HudRunSummary> Runs,
    DateTimeOffset UpdatedAt)
{
    public static HudSnapshot Unknown { get; } =
        new(HudConnectionState.Unknown, [], DateTimeOffset.UtcNow);

    /// <summary>First non-terminal tracked run, if any.</summary>
    public HudRunSummary? ActiveRun => Runs.FirstOrDefault(r => r.IsActive);

    public HudDotKind Dot => Connection switch
    {
        HudConnectionState.Connected when ActiveRun is not null => HudDotKind.Working,
        HudConnectionState.Connected => HudDotKind.Ok,
        HudConnectionState.Disconnected => HudDotKind.Error,
        _ => HudDotKind.Unknown,
    };

    public string StatusText
    {
        get
        {
            var connection = Connection switch
            {
                HudConnectionState.Connected => "Gateway connected",
                HudConnectionState.Disconnected => "Gateway unreachable",
                _ => "Checking gateway…",
            };

            var run = ActiveRun;
            return run is null ? connection : $"{connection} · run {run.ShortId} {run.Status}";
        }
    }

    /// <summary>
    /// Material-change comparison for <c>SnapshotChanged</c> gating: the
    /// timestamp is excluded so identical states don't re-render.
    /// </summary>
    public bool SameContentAs(HudSnapshot? other)
    {
        if (other is null || other.Connection != Connection)
        {
            return false;
        }

        if (other.Runs.Count != Runs.Count)
        {
            return false;
        }

        for (var i = 0; i < Runs.Count; i++)
        {
            var a = Runs[i];
            var b = other.Runs[i];
            if (a.RunId != b.RunId || a.Status != b.Status || a.IsActive != b.IsActive)
            {
                return false;
            }
        }

        return true;
    }
}
