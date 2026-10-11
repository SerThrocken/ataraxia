namespace Hermes.Client.Core;

/// <summary>
/// JSON-backed per-name settings blobs under %LocalAppData%\Hermes.Client.
/// </summary>
public interface ISettingsService
{
    Task<T?> LoadAsync<T>(string name, CancellationToken cancellationToken = default);
    Task SaveAsync<T>(string name, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raw JSON access: the blob verbatim (for backup/export), or null
    /// when no blob is stored under <paramref name="name"/>.
    /// </summary>
    Task<string?> LoadRawAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Writes the blob verbatim; <paramref name="json"/> must be valid JSON.</summary>
    Task SaveRawAsync(string name, string json, CancellationToken cancellationToken = default);
}
