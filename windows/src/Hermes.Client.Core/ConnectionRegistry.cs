namespace Hermes.Client.Core;

/// <summary>
/// One saved Hermes gateway: display name, host, port, TLS flag. The Bearer
/// API key is never stored here — it lives in <see cref="ISecretStore"/>
/// under <see cref="SecretAlias"/> (one alias per connection id).
/// </summary>
public sealed record GatewayConnection
{
    /// <summary>Stable id; the registry generates one when blank.</summary>
    public string Id { get; init; } = string.Empty;

    public required string Name { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; } = GatewayConnectionRegistry.DefaultGatewayPort;

    public bool UseTls { get; init; }

    /// <summary>Secret-store alias holding this connection's Bearer API key.</summary>
    public string SecretAlias => $"gateway-api-key:{Id}";

    /// <summary>Full base URL, e.g. http://100.101.250.118:8642/.</summary>
    public Uri BaseUrl => new UriBuilder(
        UseTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, Host.Trim(), Port).Uri;
}

/// <summary>
/// Shape of the pre-registry single-connection settings blob
/// (WinUI3 <c>SettingsViewModel.SettingsName</c> = "connection").
/// </summary>
public sealed record LegacyConnectionSettings
{
    public string? BaseUrl { get; init; }
}

/// <summary>
/// Registry of saved gateway connections, persisted as a JSON blob via
/// <see cref="ISettingsService"/> (no secrets in the blob — those live in
/// <see cref="ISecretStore"/> under per-connection aliases). The first
/// connection added becomes the active one.
/// </summary>
public sealed class GatewayConnectionRegistry
{
    public const string SettingsName = "gateway-connections";

    /// <summary>CLI gateway port, per SCOPE.md §2.</summary>
    public const int DefaultGatewayPort = 8642;

    private const string ImportedId = "imported";

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    public GatewayConnectionRegistry(ISettingsService settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    private sealed record State
    {
        public string? ActiveId { get; init; }
        public List<GatewayConnection> Connections { get; init; } = new();
        public bool MigratedFromSingleConnection { get; init; }
    }

    public async Task<IReadOnlyList<GatewayConnection>> ListAsync(
        CancellationToken cancellationToken = default) =>
        (await LoadStateAsync(cancellationToken).ConfigureAwait(false)).Connections;

    public async Task<GatewayConnection?> GetAsync(
        string id, CancellationToken cancellationToken = default) =>
        (await LoadStateAsync(cancellationToken).ConfigureAwait(false))
        .Connections.FirstOrDefault(c => c.Id == id);

    public async Task<GatewayConnection?> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        return state.ActiveId is null
            ? null
            : state.Connections.FirstOrDefault(c => c.Id == state.ActiveId);
    }

    public async Task SetActiveIdAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Connections.All(c => c.Id != id))
        {
            throw new KeyNotFoundException($"No gateway connection with id '{id}'.");
        }

        await SaveStateAsync(state with { ActiveId = id }, cancellationToken).ConfigureAwait(false);
    }

    /// <returns>The stored connection's id (generated when blank).</returns>
    public async Task<string> AddAsync(
        GatewayConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Validate(connection);
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        var id = string.IsNullOrWhiteSpace(connection.Id)
            ? NewId()
            : connection.Id.Trim();
        if (state.Connections.Any(c => c.Id == id))
        {
            throw new InvalidOperationException(
                $"A gateway connection with id '{id}' already exists.");
        }

        var stored = connection with
        {
            Id = id,
            Name = connection.Name.Trim(),
            Host = connection.Host.Trim(),
        };
        state.Connections.Add(stored);
        await SaveStateAsync(
            state with { ActiveId = state.ActiveId ?? id }, cancellationToken)
            .ConfigureAwait(false);
        return id;
    }

    public async Task UpdateAsync(
        GatewayConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Id);
        Validate(connection);
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        var index = state.Connections.FindIndex(c => c.Id == connection.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException(
                $"No gateway connection with id '{connection.Id}'.");
        }

        state.Connections[index] = connection with
        {
            Name = connection.Name.Trim(),
            Host = connection.Host.Trim(),
        };
        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the connection and deletes its API key from the secret store.
    /// </summary>
    public async Task DeleteAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        var removed = state.Connections.RemoveAll(c => c.Id == id);
        if (removed == 0)
        {
            throw new KeyNotFoundException($"No gateway connection with id '{id}'.");
        }

        await _secrets.DeleteAsync($"gateway-api-key:{id}", cancellationToken)
            .ConfigureAwait(false);
        var activeId = state.ActiveId == id
            ? state.Connections.FirstOrDefault()?.Id
            : state.ActiveId;
        await SaveStateAsync(state with { ActiveId = activeId }, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<string?> GetApiKeyAsync(
        string id, CancellationToken cancellationToken = default) =>
        _secrets.GetAsync($"gateway-api-key:{id}", cancellationToken);

    public Task SetApiKeyAsync(
        string id, string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return _secrets.SetAsync($"gateway-api-key:{id.Trim()}", apiKey.Trim(), cancellationToken);
    }

    public Task DeleteApiKeyAsync(
        string id, CancellationToken cancellationToken = default) =>
        _secrets.DeleteAsync($"gateway-api-key:{id}", cancellationToken);

    /// <summary>
    /// One-time migration: the pre-registry single connection (URL in the
    /// legacy settings blob, key in the legacy secret alias) becomes the
    /// first registry entry. The key is <em>moved</em>, not copied — after
    /// migration it lives only under the new per-connection alias.
    /// Idempotent: returns false when there is nothing to import or the
    /// migration already ran.
    /// </summary>
    public async Task<bool> EnsureMigratedFromSingleConnectionAsync(
        string legacySettingsName,
        string legacySecretName,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.MigratedFromSingleConnection)
        {
            return false;
        }

        var migrated = false;
        var legacy = await _settings.LoadAsync<LegacyConnectionSettings>(
            legacySettingsName, cancellationToken).ConfigureAwait(false);
        if (legacy?.BaseUrl is { } raw &&
            Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var id = state.Connections.Any(c => c.Id == ImportedId) ? NewId() : ImportedId;
            await AddAsync(new GatewayConnection
            {
                Id = id,
                Name = $"Imported ({uri.Host})",
                Host = uri.Host,
                Port = uri.IsDefaultPort ? DefaultGatewayPort : uri.Port,
                UseTls = uri.Scheme == Uri.UriSchemeHttps,
            }, cancellationToken).ConfigureAwait(false);

            var key = await _secrets.GetAsync(legacySecretName, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(key))
            {
                await _secrets.SetAsync($"gateway-api-key:{id}", key, cancellationToken)
                    .ConfigureAwait(false);
                await _secrets.DeleteAsync(legacySecretName, cancellationToken)
                    .ConfigureAwait(false);
            }

            migrated = true;
        }

        state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        await SaveStateAsync(state with { MigratedFromSingleConnection = true }, cancellationToken)
            .ConfigureAwait(false);
        return migrated;
    }

    private static void Validate(GatewayConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Name))
        {
            throw new ArgumentException("Connection name is required.", nameof(connection));
        }

        if (string.IsNullOrWhiteSpace(connection.Host) ||
            Uri.CheckHostName(connection.Host.Trim()) == UriHostNameType.Unknown)
        {
            throw new ArgumentException(
                $"'{connection.Host}' is not a valid host name or IP address.",
                nameof(connection));
        }

        if (connection.Port is < 1 or > 65535)
        {
            throw new ArgumentException(
                "Port must be between 1 and 65535.", nameof(connection));
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private async Task<State> LoadStateAsync(CancellationToken cancellationToken) =>
        await _settings.LoadAsync<State>(SettingsName, cancellationToken).ConfigureAwait(false)
        ?? new State();

    private Task SaveStateAsync(State state, CancellationToken cancellationToken) =>
        _settings.SaveAsync(SettingsName, state, cancellationToken);
}
