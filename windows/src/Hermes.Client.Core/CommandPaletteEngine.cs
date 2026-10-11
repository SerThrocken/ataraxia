namespace Hermes.Client.Core;

/// <summary>What a command-palette row points at.</summary>
public enum PaletteItemKind
{
    Skill,
    Session,
    Page,
    Action,
}

/// <summary>
/// One row in the command palette. Payload is the skill name, session id,
/// page tag (matches the shell's NavigationView tags), or action id.
/// </summary>
public sealed record PaletteItem(PaletteItemKind Kind, string Title, string? Subtitle, string Payload)
{
    public string KindLabel => Kind switch
    {
        PaletteItemKind.Skill => "Skill",
        PaletteItemKind.Session => "Session",
        PaletteItemKind.Page => "Page",
        PaletteItemKind.Action => "Action",
        _ => "",
    };
}

/// <summary>What the host must do when a palette row is chosen.</summary>
public enum PaletteEffectKind
{
    InsertText,
    OpenSession,
    NavigatePage,
    NewSession,
    Reconnect,
}

/// <summary>
/// Host-executed outcome of choosing a palette row. Payload is the text to
/// insert, the session id, or the page tag; Label carries the session title
/// for <see cref="PaletteEffectKind.OpenSession"/>.
/// </summary>
public sealed record PaletteEffect(PaletteEffectKind Kind, string Payload, string? Label);

/// <summary>
/// Pure command-palette logic (no UI): builds rows from skills / sessions /
/// static pages+actions, filters+ranks them per query, maps a chosen row to
/// the host effect, and fetches remote rows defensively — the palette must
/// never fail to open (GET /v1/skills currently 500s; see api-contract.md).
/// </summary>
public sealed class CommandPaletteEngine
{
    public const string ActionNewSession = "new-session";
    public const string ActionReconnect = "reconnect";

    private static readonly IReadOnlyList<PaletteItem> StaticItems =
    [
        new(PaletteItemKind.Action, "New session", "Start a new chat session", ActionNewSession),
        new(PaletteItemKind.Action, "Reconnect", "Test the gateway connection", ActionReconnect),
        new(PaletteItemKind.Page, "Go to Chat", "Open the chat page", "chat"),
        new(PaletteItemKind.Page, "Go to Sessions", "Browse past sessions", "sessions"),
        new(PaletteItemKind.Page, "Go to Cron jobs", "Scheduled cron jobs", "cron"),
        new(PaletteItemKind.Page, "Go to Models", "View available models", "models"),
        new(PaletteItemKind.Page, "Go to Skills", "Browse installed skills", "skills"),
        new(PaletteItemKind.Page, "Search Sessions", "Full-text search over sessions", "search"),
        new(PaletteItemKind.Page, "Go to Terminal", "Terminal backend settings", "terminal"),
        new(PaletteItemKind.Page, "Go to Voice", "Voice I/O settings", "voice"),
        new(PaletteItemKind.Page, "Go to Memory", "Memory and vault", "memory"),
        new(PaletteItemKind.Page, "Go to Health", "Gateway health dashboard", "health"),
        new(PaletteItemKind.Page, "Go to Analytics", "Cost analytics", "analytics"),
        new(PaletteItemKind.Page, "Go to Providers", "Provider health board", "providers"),
        new(PaletteItemKind.Page, "Run Diagnostics", "One-tap gateway diagnostics", "diagnostics"),
        new(PaletteItemKind.Page, "Open Settings", "Connection and app settings", "settings"),
    ];

    /// <summary>Curated default order: actions, pages, skills, sessions.</summary>
    public IReadOnlyList<PaletteItem> BuildItems(
        IReadOnlyList<SkillInfo> skills,
        IReadOnlyList<Session> sessions)
    {
        var items = new List<PaletteItem>(StaticItems);
        items.AddRange(skills.Select(s =>
            new PaletteItem(PaletteItemKind.Skill, s.Name, s.Description, s.Name)));
        items.AddRange(sessions.Select(s => new PaletteItem(
            PaletteItemKind.Session,
            string.IsNullOrWhiteSpace(s.Title) ? s.Preview ?? s.Id : s.Title,
            $"{s.MessageCount} messages",
            s.Id)));
        return items;
    }

    /// <summary>Filters by query and ranks best-first; empty query keeps build order.</summary>
    public IReadOnlyList<PaletteItem> Query(IReadOnlyList<PaletteItem> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return items;
        }

        return items
            .Select(i => (Item: i, Score: BestScore(query, i)))
            .Where(t => t.Score.HasValue)
            .OrderByDescending(t => t.Score)
            .ThenBy(t => t.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Item)
            .ToList();
    }

    /// <summary>Maps a chosen row to the effect the host must perform.</summary>
    public PaletteEffect EffectFor(PaletteItem item) => item.Kind switch
    {
        PaletteItemKind.Skill => new PaletteEffect(PaletteEffectKind.InsertText, "/" + item.Payload + " ", null),
        PaletteItemKind.Session => new PaletteEffect(PaletteEffectKind.OpenSession, item.Payload, item.Title),
        PaletteItemKind.Page => new PaletteEffect(PaletteEffectKind.NavigatePage, item.Payload, null),
        PaletteItemKind.Action when item.Payload == ActionNewSession => new PaletteEffect(PaletteEffectKind.NewSession, "", null),
        PaletteItemKind.Action when item.Payload == ActionReconnect => new PaletteEffect(PaletteEffectKind.Reconnect, "", null),
        _ => throw new ArgumentOutOfRangeException(nameof(item), $"Unknown palette item: {item.Kind}/{item.Payload}"),
    };

    /// <summary>GET /v1/skills, degraded to empty on any failure (it 500s today).</summary>
    public static async Task<IReadOnlyList<SkillInfo>> GetSkillsSafeAsync(
        IGatewayClient client,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.GetSkillsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>GET /api/sessions, degraded to empty when the gateway is unreachable.</summary>
    public static async Task<IReadOnlyList<Session>> GetSessionsSafeAsync(
        IGatewayClient client,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static int? BestScore(string query, PaletteItem item)
    {
        var title = PaletteMatcher.Score(query, item.Title);
        var subtitle = PaletteMatcher.Score(query, item.Subtitle);
        if (subtitle.HasValue)
        {
            subtitle -= 25; // title matches outrank subtitle matches
        }

        return title.HasValue && subtitle.HasValue
            ? Math.Max(title.Value, subtitle.Value)
            : title ?? subtitle;
    }
}
