namespace Hermes.Client.Core;

/// <summary>
/// Validated, secret-free SSH dial parameters built from a saved
/// <see cref="SshHost"/>. This is the unit under test for "SSH option
/// building/parsing without live connections": everything here is pure
/// validation and string handling — no sockets, no keys.
/// </summary>
public sealed record SshConnectionOptions
{
    public required string Host { get; init; }

    public int Port { get; init; } = SshHostRegistry.DefaultSshPort;

    public required string Username { get; init; }

    public SshAuthKind AuthKind { get; init; } = SshAuthKind.PrivateKey;

    public static SshConnectionOptions FromHost(SshHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new SshConnectionOptions
        {
            Host = host.Host,
            Port = host.Port,
            Username = host.Username,
            AuthKind = host.AuthKind,
        };
    }

    /// <returns>Null when the options are valid; otherwise a human-readable reason.</returns>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) ||
            Uri.CheckHostName(Host.Trim()) == UriHostNameType.Unknown)
        {
            return $"Host '{Host}' is not a valid host name or IP address.";
        }

        if (Port is < 1 or > 65535)
        {
            return "Port must be between 1 and 65535.";
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            return "Username is required.";
        }

        return null;
    }

    /// <summary>
    /// Log-safe description (<c>user@host:port</c>). Never includes secrets —
    /// there are none on this record by construction.
    /// </summary>
    public string Describe() => $"{Username.Trim()}@{Host.Trim()}:{Port}";
}
