namespace Hermes.Client.Core;

/// <summary>Outcome of <see cref="GatewaySwitcher.SwitchToAsync"/>.</summary>
public enum SwitchOutcome
{
    Switched,
    AlreadyActive,
}

/// <summary>
/// Owns the active gateway connection. Switching tears down the old
/// connection's live resources (SSE streams, run subscriptions, anything
/// the UI registered as <see cref="IAsyncDisposable"/>) <em>before</em> the
/// shared client is re-pointed at the new gateway — no session leakage, no
/// cross-talk between gateways. The switcher also runs the one-time
/// migration of the pre-registry single connection on first launch.
/// </summary>
public sealed class GatewaySwitcher
{
    private readonly GatewayConnectionRegistry _registry;

    public GatewaySwitcher(GatewayConnectionRegistry registry)
    {
        _registry = registry;
    }

    public GatewayConnection? Active { get; private set; }

    public event EventHandler<GatewayConnection>? Switched;

    /// <summary>
    /// First-launch wiring: migrates the legacy single connection into the
    /// registry (import, never orphan), then points <paramref name="endpoint"/>
    /// at the active registry entry. Leaves the endpoint untouched when the
    /// registry is empty and no legacy connection exists.
    /// </summary>
    public async Task InitializeAsync(
        IGatewayEndpoint endpoint,
        string legacySettingsName,
        string legacySecretName,
        CancellationToken cancellationToken = default)
    {
        await _registry.EnsureMigratedFromSingleConnectionAsync(
            legacySettingsName, legacySecretName, cancellationToken).ConfigureAwait(false);
        var active = await _registry.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (active is not null)
        {
            await ApplyAsync(endpoint, active, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Switches to another saved gateway. Disposes <paramref name="liveResources"/>
    /// first so the old connection's sessions/streams/subscriptions are fully
    /// torn down before the client talks to the new gateway.
    /// </summary>
    public async Task<SwitchOutcome> SwitchToAsync(
        string id,
        IGatewayEndpoint endpoint,
        IAsyncDisposable? liveResources,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(endpoint);
        var target = await _registry.GetAsync(id.Trim(), cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No gateway connection with id '{id}'.");
        if (Active?.Id == target.Id)
        {
            return SwitchOutcome.AlreadyActive;
        }

        if (liveResources is not null)
        {
            await liveResources.DisposeAsync().ConfigureAwait(false);
        }

        await ApplyAsync(endpoint, target, cancellationToken).ConfigureAwait(false);
        return SwitchOutcome.Switched;
    }

    private async Task ApplyAsync(
        IGatewayEndpoint endpoint, GatewayConnection connection, CancellationToken cancellationToken)
    {
        endpoint.SetBaseUrl(connection.BaseUrl);
        endpoint.SetApiKey(await _registry.GetApiKeyAsync(connection.Id, cancellationToken)
            .ConfigureAwait(false));
        await _registry.SetActiveIdAsync(connection.Id, cancellationToken).ConfigureAwait(false);
        Active = connection;
        Switched?.Invoke(this, connection);
    }
}
