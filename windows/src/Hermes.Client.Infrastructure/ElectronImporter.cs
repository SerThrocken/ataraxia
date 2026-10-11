using Hermes.Client.Core;

namespace Hermes.Client.Infrastructure;

/// <summary>
/// One Electron connection as it would import: the resolved gateway URL
/// plus key availability. Carries no secret values — only lengths —
/// so it is safe to display and to log.
/// </summary>
public sealed record ElectronConnectionPreview(
    string Id,
    string Kind,
    string Label,
    string? ResolvedBaseUrl,
    bool KeyAvailable,
    int KeyLength,
    string KeySourceDescription);

/// <summary>
/// Read-only scan result: what the Electron app has that we can import.
/// Produced without writing anything and without exposing secret values.
/// </summary>
public sealed record ElectronImportPreview(
    IReadOnlyList<ElectronConnectionPreview> Connections,
    string? PrimaryId,
    IReadOnlyList<string> SettingsFound,
    string SourceDirectory);

/// <summary>What the user chose to import on the import screen.</summary>
public sealed record ElectronImportSelection(
    string? ConnectionId,
    bool ImportSettings);

/// <summary>Outcome counts only — never secret values.</summary>
public sealed record ElectronImportResult(
    bool ConnectionImported,
    bool KeyImported,
    IReadOnlyList<string> SettingsImported,
    string Summary);

/// <summary>
/// JSON shape of the imported connection blob. Mirrors the WinUI3
/// <c>ConnectionSettings</c> shape ({ BaseUrl }) so the existing settings
/// page reads it back unchanged.
/// </summary>
public sealed record ImportedConnectionSettings
{
    public string BaseUrl { get; init; } = string.Empty;
}

/// <summary>
/// Imported appearance settings, stored under the "appearance" blob.
/// The app does not apply them yet — import persists them for later use.
/// </summary>
public sealed record ImportedAppearanceSettings
{
    public string? Theme { get; init; }
    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }
    public bool? WindowMaximized { get; init; }
}

/// <summary>
/// Migrates connections, API keys, and desktop settings from the Electron
/// Hermes desktop app into this client's stores. Secrets move from
/// plaintext (Electron token storage / gateway .env) straight into the
/// OS secret store (DPAPI) — they are never logged, never written to
/// settings files, and never held longer than the import call.
/// </summary>
public sealed class ElectronImporter
{
    /// <summary>
    /// The Electron "local" connection spawns the gateway on this machine;
    /// the native equivalent is loopback.
    /// </summary>
    public const string LoopbackBaseUrl = "http://127.0.0.1:8642";

    private readonly ElectronDataPaths _paths;
    private readonly ISecretStore _secrets;
    private readonly ISettingsService _settings;
    private readonly string _apiKeySecretName;
    private readonly string _connectionSettingsName;

    public ElectronImporter(
        ElectronDataPaths paths,
        ISecretStore secrets,
        ISettingsService settings,
        string apiKeySecretName = "gateway-api-key",
        string connectionSettingsName = "connection")
    {
        _paths = paths;
        _secrets = secrets;
        _settings = settings;
        _apiKeySecretName = apiKeySecretName;
        _connectionSettingsName = connectionSettingsName;
    }

    /// <summary>
    /// Read-only scan of the Electron data. Safe to call any number of
    /// times; writes nothing.
    /// </summary>
    public Task<ElectronImportPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var registry = ElectronRegistryReader.ReadRegistry(_paths.ElectronAppDataDir);
        var legacy = ElectronRegistryReader.ReadLegacyRemoteConnection(_paths.ElectronAppDataDir);
        var tokens = ElectronRegistryReader.ReadTokenStorage(_paths.ElectronAppDataDir);
        var gatewayKey = ElectronRegistryReader.ReadGatewayApiKey(_paths.HermesLocalDir);
        var desktop = ElectronRegistryReader.ReadDesktopSettings(_paths.ElectronAppDataDir);

        var connections = registry.Connections.ToList();
        if (legacy is not null && connections.All(c => c.Id != legacy.Id))
        {
            connections.Add(legacy);
        }

        var previews = connections
            .Select(c => BuildPreview(c, tokens, gatewayKey))
            .ToList();

        return Task.FromResult(new ElectronImportPreview(
            previews,
            registry.PrimaryId,
            DescribeSettings(desktop),
            _paths.ElectronAppDataDir));
    }

    /// <summary>
    /// Imports the chosen connection (URL → settings blob, key → DPAPI)
    /// and, when selected, the desktop settings. Secret values pass
    /// through memory only, straight into <see cref="ISecretStore"/>.
    /// </summary>
    public async Task<ElectronImportResult> ImportAsync(
        ElectronImportSelection selection,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(cancellationToken).ConfigureAwait(false);
        var chosen = preview.Connections.FirstOrDefault(c => c.Id == selection.ConnectionId)
            ?? preview.Connections.FirstOrDefault(c => c.Id == preview.PrimaryId)
            ?? preview.Connections.FirstOrDefault();

        var connectionImported = false;
        var keyImported = false;
        var settingsImported = new List<string>();

        if (chosen?.ResolvedBaseUrl is { } baseUrl &&
            ConnectionValidation.IsValidBaseUrl(baseUrl))
        {
            await _settings.SaveAsync(
                _connectionSettingsName,
                new ImportedConnectionSettings { BaseUrl = baseUrl },
                cancellationToken).ConfigureAwait(false);
            connectionImported = true;
        }

        // Re-resolve the key from disk rather than trusting the preview:
        // the values never sit in the preview object.
        if (chosen is not null)
        {
            var resolution = ResolveKey(
                chosen.Kind,
                ElectronRegistryReader.ReadTokenStorage(_paths.ElectronAppDataDir),
                ElectronRegistryReader.ReadGatewayApiKey(_paths.HermesLocalDir));
            if (resolution.Value is not null)
            {
                await _secrets.SetAsync(_apiKeySecretName, resolution.Value, cancellationToken).ConfigureAwait(false);
                keyImported = true;
            }
        }

        if (selection.ImportSettings)
        {
            var desktop = ElectronRegistryReader.ReadDesktopSettings(_paths.ElectronAppDataDir);
            if (desktop.Theme is not null ||
                desktop.WindowWidth is not null ||
                desktop.WindowMaximized is not null)
            {
                await _settings.SaveAsync("appearance", new ImportedAppearanceSettings
                {
                    Theme = desktop.Theme,
                    WindowWidth = desktop.WindowWidth,
                    WindowHeight = desktop.WindowHeight,
                    WindowMaximized = desktop.WindowMaximized,
                }, cancellationToken).ConfigureAwait(false);
                settingsImported.AddRange(DescribeSettings(desktop));
            }
        }

        var summary = connectionImported || keyImported || settingsImported.Count > 0
            ? $"Imported connection '{chosen?.Label}'" +
              (connectionImported ? $" ({chosen?.ResolvedBaseUrl})" : " (URL unavailable — skipped)") +
              (keyImported ? "; API key saved encrypted (DPAPI)" : "; no API key found") +
              (settingsImported.Count > 0
                  ? $"; settings: {string.Join(", ", settingsImported)}"
                  : "; no settings imported")
            : "Nothing to import — the Electron app has no connections stored.";

        return new ElectronImportResult(connectionImported, keyImported, settingsImported, summary);
    }

    private static ElectronConnectionPreview BuildPreview(
        ElectronConnection connection,
        IReadOnlyDictionary<string, string> tokens,
        string? gatewayKey)
    {
        var resolvedUrl = ResolveBaseUrl(connection);
        var resolution = ResolveKey(connection.Kind, tokens, gatewayKey);

        return new ElectronConnectionPreview(
            connection.Id,
            connection.Kind,
            connection.Label,
            resolvedUrl,
            resolution.Value is not null,
            resolution.Value?.Length ?? 0,
            resolution.Source);
    }

    private static string? ResolveBaseUrl(ElectronConnection connection)
    {
        if (connection.Kind.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return LoopbackBaseUrl;
        }

        return ConnectionValidation.IsValidBaseUrl(connection.Url) ? connection.Url!.Trim() : null;
    }

    /// <summary>
    /// Finds a usable API key for the connection, with a human-readable
    /// description of where it came from (never the value). Token-storage
    /// values are only trusted when they look like real keys (≥16 chars,
    /// not a flag); the local gateway's .env is the fallback for local
    /// connections. Callers must not log <see cref="KeyResolution.Value"/>.
    /// </summary>
    private sealed record KeyResolution(string? Value, string Source);

    private static KeyResolution ResolveKey(
        string kind,
        IReadOnlyDictionary<string, string> tokens,
        string? gatewayKey)
    {
        const string tokenStorageSource = "Electron token storage (plaintext — will be encrypted on import)";

        string? TokenLike(string? value) =>
            !string.IsNullOrEmpty(value) && value.Length >= ConnectionValidation.MinApiKeyLength
                ? value
                : null;

        foreach (var pair in tokens)
        {
            if (TokenLike(pair.Value) is { } match &&
                pair.Key.Contains(kind, StringComparison.OrdinalIgnoreCase))
            {
                return new KeyResolution(match, tokenStorageSource);
            }
        }

        var tokenLikes = tokens.Values.Select(TokenLike).OfType<string>().ToList();
        if (tokenLikes.Count == 1)
        {
            return new KeyResolution(tokenLikes[0], tokenStorageSource);
        }

        if (kind.Equals("local", StringComparison.OrdinalIgnoreCase) &&
            TokenLike(gatewayKey) is { } localKey)
        {
            return new KeyResolution(localKey, "local gateway .env (will be encrypted on import)");
        }

        return new KeyResolution(null, "not found");
    }

    private static IReadOnlyList<string> DescribeSettings(ElectronDesktopSettings desktop)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(desktop.Theme))
        {
            lines.Add($"Theme: {desktop.Theme}");
        }

        if (desktop.WindowWidth is { } width && desktop.WindowHeight is { } height)
        {
            lines.Add($"Window size: {width}×{height}");
        }

        if (desktop.WindowMaximized is true)
        {
            lines.Add("Window: maximized");
        }

        return lines;
    }
}
