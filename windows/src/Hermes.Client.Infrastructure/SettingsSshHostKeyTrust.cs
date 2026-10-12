using Hermes.Client.Core;

namespace Hermes.Client.Infrastructure;

/// <summary>
/// <see cref="ISshHostKeyTrust"/> backed by the JSON settings blob
/// <c>ssh-known-hosts</c>: <c>{"host:port": "SHA256:..."}</c>.
/// Pins are per host+port (lowercased host); the blob holds fingerprints
/// only — never keys, never secrets.
/// </summary>
public sealed class SettingsSshHostKeyTrust : ISshHostKeyTrust
{
    private const string SettingsName = "ssh-known-hosts";

    private readonly ISettingsService _settings;

    public SettingsSshHostKeyTrust(ISettingsService settings)
    {
        _settings = settings;
    }

    private sealed record KnownHosts
    {
        public Dictionary<string, string> Pins { get; init; } = new();
    }

    public async Task<string?> GetPinnedFingerprintAsync(
        string host, int port, CancellationToken cancellationToken = default)
    {
        var known = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return known.Pins.TryGetValue(KeyFor(host, port), out var fingerprint)
            ? fingerprint
            : null;
    }

    public async Task PinFingerprintAsync(
        string host, int port, string fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var known = await LoadAsync(cancellationToken).ConfigureAwait(false);
        known.Pins[KeyFor(host, port)] = fingerprint.Trim();
        await _settings.SaveAsync(SettingsName, known, cancellationToken).ConfigureAwait(false);
    }

    private static string KeyFor(string host, int port) =>
        $"{host.Trim().ToLowerInvariant()}:{port}";

    private async Task<KnownHosts> LoadAsync(CancellationToken cancellationToken) =>
        await _settings.LoadAsync<KnownHosts>(SettingsName, cancellationToken)
            .ConfigureAwait(false) ?? new KnownHosts();
}
