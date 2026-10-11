using System.IO;
using System.Text.Json;

namespace Ataraxia.PowerToysRun;

/// <summary>
/// The plugin's own settings (<c>settings.json</c> next to <c>plugin.json</c>,
/// hand-edited). Only an override lives here: by default the plugin reads the
/// gateway URL the Ataraxia app saved (shared <c>connection.json</c>) and the
/// API key from the shared DPAPI secret store. The key is never in this file.
/// </summary>
internal sealed record PluginSettings
{
    /// <summary>
    /// Optional gateway base-URL override (e.g. <c>http://127.0.0.1:8642</c>).
    /// Empty/missing = use the Ataraxia app's saved connection.
    /// </summary>
    public string? GatewayUrl { get; init; }

    public static PluginSettings Load(string pluginDirectory)
    {
        try
        {
            var path = Path.Combine(pluginDirectory, "settings.json");
            if (!File.Exists(path))
            {
                return new PluginSettings();
            }

            return JsonSerializer.Deserialize<PluginSettings>(
                File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new PluginSettings();
        }
        catch
        {
            // A broken settings file must not break the plugin.
            return new PluginSettings();
        }
    }
}

/// <summary>
/// The shape of the connection blob the Ataraxia app writes via its own
/// settings service (settings name <c>connection</c>, camelCase JSON).
/// Defined locally so the plugin does not reference the WinUI3 app.
/// </summary>
internal sealed record SharedConnectionSettings
{
    public string? BaseUrl { get; init; }
}
