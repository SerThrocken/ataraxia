namespace Ataraxia.PowerToysRun;

/// <summary>
/// What the user asked for, parsed from the text after the <c>ax</c> keyword.
/// Pure data — no I/O, no PowerToys types — so it unit-tests anywhere.
/// </summary>
internal abstract record PluginIntent;

/// <summary>Empty query: show recent sessions + quick actions.</summary>
internal sealed record HomeIntent : PluginIntent;

/// <summary>Free text: search session titles.</summary>
internal sealed record SessionSearchIntent(string Text) : PluginIntent;

/// <summary><c>!message</c> or <c>ask message</c>: send to a brand-new session.</summary>
internal sealed record QuickSendIntent(string Message) : PluginIntent;

/// <summary><c>open</c>: launch the Ataraxia app.</summary>
internal sealed record OpenAppIntent : PluginIntent;

/// <summary>
/// Maps the raw search string (PowerToys <c>Query.Search</c>, i.e. everything
/// after the action keyword) to a <see cref="PluginIntent"/>.
/// </summary>
internal static class QueryRouter
{
    public static PluginIntent Route(string? search)
    {
        var text = (search ?? "").Trim();
        if (text.Length == 0)
        {
            return new HomeIntent();
        }

        if (text.StartsWith('!'))
        {
            var message = text[1..].Trim();
            return message.Length == 0 ? new HomeIntent() : new QuickSendIntent(message);
        }

        if (text.Equals("ask", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("ask ", StringComparison.OrdinalIgnoreCase))
        {
            var message = text.Length <= 3 ? "" : text[4..].Trim();
            return message.Length == 0 ? new HomeIntent() : new QuickSendIntent(message);
        }

        if (text.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenAppIntent();
        }

        return new SessionSearchIntent(text);
    }
}
