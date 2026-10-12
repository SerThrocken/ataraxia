namespace Hermes.Client.Core;

/// <summary>How an SSH host authenticates: private key or password.</summary>
public enum SshAuthKind
{
    PrivateKey,
    Password,
}

/// <summary>
/// One saved SSH host: display name, host, port, username, auth kind.
/// Secrets (private key, optional key passphrase, password) are never
/// stored here — they live in <see cref="ISecretStore"/> under the
/// per-host aliases below, encrypted by DPAPI (Windows) / Keystore (Android).
/// </summary>
public sealed record SshHost
{
    /// <summary>Stable id; the registry generates one when blank.</summary>
    public string Id { get; init; } = string.Empty;

    public required string Name { get; init; }

    public required string Host { get; init; }

    public int Port { get; init; } = SshHostRegistry.DefaultSshPort;

    public required string Username { get; init; }

    public SshAuthKind AuthKind { get; init; } = SshAuthKind.PrivateKey;

    /// <summary>Secret-store alias for the OpenSSH-format private key.</summary>
    public string PrivateKeyAlias => $"ssh-private-key:{Id}";

    /// <summary>Secret-store alias for the private key's passphrase (optional).</summary>
    public string KeyPassphraseAlias => $"ssh-key-passphrase:{Id}";

    /// <summary>Secret-store alias for password auth.</summary>
    public string PasswordAlias => $"ssh-password:{Id}";
}

/// <summary>
/// Registry of saved SSH hosts, persisted as a JSON blob via
/// <see cref="ISettingsService"/> (no secrets in the blob).
/// </summary>
public sealed class SshHostRegistry
{
    public const string SettingsName = "ssh-hosts";

    /// <summary>Default SSH port.</summary>
    public const int DefaultSshPort = 22;

    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;

    public SshHostRegistry(ISettingsService settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    private sealed record State
    {
        public List<SshHost> Hosts { get; init; } = new();
    }

    public async Task<IReadOnlyList<SshHost>> ListAsync(
        CancellationToken cancellationToken = default) =>
        (await LoadStateAsync(cancellationToken).ConfigureAwait(false)).Hosts;

    public async Task<SshHost?> GetAsync(
        string id, CancellationToken cancellationToken = default) =>
        (await LoadStateAsync(cancellationToken).ConfigureAwait(false))
        .Hosts.FirstOrDefault(h => h.Id == id);

    /// <returns>The stored host's id (generated when blank).</returns>
    public async Task<string> AddAsync(
        SshHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var invalid = SshConnectionOptions.FromHost(host).Validate();
        if (invalid is not null)
        {
            throw new ArgumentException(invalid, nameof(host));
        }

        if (string.IsNullOrWhiteSpace(host.Name))
        {
            throw new ArgumentException("Host name is required.", nameof(host));
        }

        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        var id = string.IsNullOrWhiteSpace(host.Id) ? NewId() : host.Id.Trim();
        if (state.Hosts.Any(h => h.Id == id))
        {
            throw new InvalidOperationException(
                $"An SSH host with id '{id}' already exists.");
        }

        var stored = host with
        {
            Id = id,
            Name = host.Name.Trim(),
            Host = host.Host.Trim(),
            Username = host.Username.Trim(),
        };
        state.Hosts.Add(stored);
        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        return id;
    }

    public async Task UpdateAsync(
        SshHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(host.Id);
        var invalid = SshConnectionOptions.FromHost(host).Validate();
        if (invalid is not null)
        {
            throw new ArgumentException(invalid, nameof(host));
        }

        if (string.IsNullOrWhiteSpace(host.Name))
        {
            throw new ArgumentException("Host name is required.", nameof(host));
        }

        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        var index = state.Hosts.FindIndex(h => h.Id == host.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"No SSH host with id '{host.Id}'.");
        }

        state.Hosts[index] = host with
        {
            Name = host.Name.Trim(),
            Host = host.Host.Trim(),
            Username = host.Username.Trim(),
        };
        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the host and deletes all of its secrets (private key,
    /// key passphrase, password) from the secret store.
    /// </summary>
    public async Task DeleteAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Hosts.RemoveAll(h => h.Id == id) == 0)
        {
            throw new KeyNotFoundException($"No SSH host with id '{id}'.");
        }

        await _secrets.DeleteAsync($"ssh-private-key:{id}", cancellationToken)
            .ConfigureAwait(false);
        await _secrets.DeleteAsync($"ssh-key-passphrase:{id}", cancellationToken)
            .ConfigureAwait(false);
        await _secrets.DeleteAsync($"ssh-password:{id}", cancellationToken)
            .ConfigureAwait(false);
        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> GetPrivateKeyAsync(
        string id, CancellationToken cancellationToken = default) =>
        _secrets.GetAsync($"ssh-private-key:{id}", cancellationToken);

    public Task SetPrivateKeyAsync(
        string id, string privateKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);
        return _secrets.SetAsync($"ssh-private-key:{id.Trim()}", privateKey, cancellationToken);
    }

    public Task<string?> GetKeyPassphraseAsync(
        string id, CancellationToken cancellationToken = default) =>
        _secrets.GetAsync($"ssh-key-passphrase:{id}", cancellationToken);

    public Task SetKeyPassphraseAsync(
        string id, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        return _secrets.SetAsync($"ssh-key-passphrase:{id.Trim()}", passphrase, cancellationToken);
    }

    public Task<string?> GetPasswordAsync(
        string id, CancellationToken cancellationToken = default) =>
        _secrets.GetAsync($"ssh-password:{id}", cancellationToken);

    public Task SetPasswordAsync(
        string id, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _secrets.SetAsync($"ssh-password:{id.Trim()}", password, cancellationToken);
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private async Task<State> LoadStateAsync(CancellationToken cancellationToken) =>
        await _settings.LoadAsync<State>(SettingsName, cancellationToken).ConfigureAwait(false)
        ?? new State();

    private Task SaveStateAsync(State state, CancellationToken cancellationToken) =>
        _settings.SaveAsync(SettingsName, state, cancellationToken);
}
