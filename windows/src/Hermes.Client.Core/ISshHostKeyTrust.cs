namespace Hermes.Client.Core;

/// <summary>
/// Pin store for SSH host keys. Fingerprints are <c>SHA256:base64</c>
/// (OpenSSH format, no padding) — never the key itself, never a secret.
/// The session factory consults this before trusting a presented host key;
/// the UI pins a fingerprint here after the user approves it.
/// </summary>
public interface ISshHostKeyTrust
{
    Task<string?> GetPinnedFingerprintAsync(
        string host, int port, CancellationToken cancellationToken = default);

    Task PinFingerprintAsync(
        string host, int port, string fingerprint, CancellationToken cancellationToken = default);
}
