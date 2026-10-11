namespace Hermes.Client.Infrastructure;

/// <summary>
/// OS-protected secret storage (Windows: DPAPI via ProtectedData, wired in
/// Phase 1). The gateway Bearer key lives here, never in plaintext config.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
