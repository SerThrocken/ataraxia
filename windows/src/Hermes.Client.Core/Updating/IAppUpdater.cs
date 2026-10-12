namespace Hermes.Client.Core;

/// <summary>
/// Self-update backend for the client app itself. The Velopack
/// implementation lives in the WinUI3 app (it needs the Velopack SDK);
/// tests substitute a fake.
/// </summary>
public interface IAppUpdater
{
    /// <summary>Version of the currently running app, for display.</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// False when no update feed URL is configured — update checks are then
    /// honestly unavailable and the UI must say so instead of failing.
    /// </summary>
    bool IsFeedConfigured { get; }

    /// <summary>Null when no newer version exists on the feed.</summary>
    Task<AppUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads <paramref name="update"/>, reporting 0..1 progress.
    /// Call <see cref="CheckForUpdatesAsync"/> first.
    /// </summary>
    Task DownloadAsync(AppUpdateInfo update, IProgress<double> progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a downloaded update and restarts the app. Does not return
    /// under the Velopack backend (the process exits).
    /// </summary>
    void ApplyAndRestart(AppUpdateInfo update);
}
