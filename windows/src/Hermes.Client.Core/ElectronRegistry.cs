using System.Text.Json;

namespace Hermes.Client.Core;

/// <summary>
/// One connection entry from the Electron app's registry
/// (%APPDATA%\Hermes\connections.json, v2).
/// </summary>
public sealed record ElectronConnection(
    string Id,
    string Kind,
    string Label,
    string? Url);

/// <summary>
/// The Electron connections registry as parsed. Missing or malformed
/// files yield an empty registry — never an exception.
/// </summary>
public sealed record ElectronRegistry(
    IReadOnlyList<ElectronConnection> Connections,
    string? PrimaryId,
    string? LastUsedId,
    int Version);

/// <summary>
/// Mappable desktop settings from the Electron app data dir.
/// Only fields with a native-client equivalent are captured:
/// theme (native-theme.json) and main-window bounds (window-state.json).
/// Electron-only state (zoom level, HUD bounds, keep-awake) is ignored.
/// </summary>
public sealed record ElectronDesktopSettings(
    string? Theme,
    int? WindowWidth,
    int? WindowHeight,
    bool? WindowMaximized);

/// <summary>
/// Pure, side-effect-free reader for the Electron Hermes desktop app's
/// on-disk state. Takes directory paths (injectable for tests); performs
/// no logging — callers must never log secret values returned here.
/// </summary>
public static class ElectronRegistryReader
{
    public const string ApiServerKeyName = "API_SERVER_KEY";

    /// <summary>
    /// Reads %APPDATA%\Hermes\connections.json (v2 registry). Returns an
    /// empty registry when the file is missing or unreadable.
    /// </summary>
    public static ElectronRegistry ReadRegistry(string electronAppDataDir)
    {
        var doc = TryReadJson(electronAppDataDir, "connections.json");
        if (doc is null)
        {
            return new ElectronRegistry([], null, null, 0);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var connections = new List<ElectronConnection>();
            if (root.TryGetProperty("connections", out var array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    connections.Add(new ElectronConnection(
                        GetString(item, "id") ?? Guid.NewGuid().ToString("N"),
                        GetString(item, "kind") ?? "unknown",
                        GetString(item, "label") ?? "Unnamed connection",
                        GetString(item, "url")));
                }
            }

            return new ElectronRegistry(
                connections,
                GetString(root, "primary"),
                GetString(root, "lastUsed"),
                root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetInt32()
                    : 0);
        }
    }

    /// <summary>
    /// Reads the legacy %APPDATA%\Hermes\connection.json (singular): the
    /// remote profile (url + auth mode). Returns null when absent.
    /// </summary>
    public static ElectronConnection? ReadLegacyRemoteConnection(string electronAppDataDir)
    {
        var doc = TryReadJson(electronAppDataDir, "connection.json");
        if (doc is null)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("remote", out var remote) ||
                remote.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var url = GetString(remote, "url");
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return new ElectronConnection(
                "remote",
                "remote",
                "Remote gateway",
                url.Trim());
        }
    }

    /// <summary>
    /// Reads %APPDATA%\Hermes\secure-token-storage.json. The Electron app
    /// runs with safeStorage OFF, so any token here is plaintext — the
    /// caller must move values straight into the OS secret store and never
    /// log or persist them elsewhere. Returns an empty map when absent.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadTokenStorage(string electronAppDataDir)
    {
        var doc = TryReadJson(electronAppDataDir, "secure-token-storage.json");
        if (doc is null)
        {
            return new Dictionary<string, string>();
        }

        using (doc)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    // Flags (e.g. "on"/"migrated") ride along in this file;
                    // the importer decides what counts as a usable token.
                    result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Reads API_SERVER_KEY from the local gateway's .env
    /// (%LOCALAPPDATA%\hermes\.env). Memory-only by convention — the
    /// caller moves it straight into DPAPI. Null when absent.
    /// </summary>
    public static string? ReadGatewayApiKey(string hermesLocalDir)
    {
        var path = Path.Combine(hermesLocalDir, ".env");
        if (!File.Exists(path))
        {
            return null;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith(ApiServerKeyName + "=", StringComparison.Ordinal))
            {
                var value = line[(ApiServerKeyName.Length + 1)..].Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the mappable subset of desktop settings: theme from
    /// native-theme.json, window bounds from window-state.json.
    /// </summary>
    public static ElectronDesktopSettings ReadDesktopSettings(string electronAppDataDir)
    {
        string? theme = null;
        var themeDoc = TryReadJson(electronAppDataDir, "native-theme.json");
        if (themeDoc is not null)
        {
            using (themeDoc)
            {
                theme = GetString(themeDoc.RootElement, "themeSource");
            }
        }

        int? width = null;
        int? height = null;
        bool? maximized = null;
        var windowDoc = TryReadJson(electronAppDataDir, "window-state.json");
        if (windowDoc is not null)
        {
            using (windowDoc)
            {
                var root = windowDoc.RootElement;
                width = GetInt(root, "width");
                height = GetInt(root, "height");
                maximized = GetBool(root, "isMaximized");
            }
        }

        return new ElectronDesktopSettings(theme, width, height, maximized);
    }

    private static JsonDocument? TryReadJson(string directory, string fileName)
    {
        try
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool? GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : null;
}
