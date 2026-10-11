namespace Hermes.Client.Infrastructure;

/// <summary>
/// JSON-backed per-name settings blobs under %LocalAppData%\Hermes.Client.
/// </summary>
public interface ISettingsService
{
    Task<T?> LoadAsync<T>(string name, CancellationToken cancellationToken = default);
    Task SaveAsync<T>(string name, T value, CancellationToken cancellationToken = default);
}
