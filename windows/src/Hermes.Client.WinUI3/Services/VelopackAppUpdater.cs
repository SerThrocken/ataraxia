using System.Reflection;
using Hermes.Client.Core;
using Velopack;
using Velopack.Exceptions;

namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// Velopack-backed <see cref="IAppUpdater"/> for Ataraxia self-updates:
/// check the feed, download (deltas when possible), apply and restart.
/// No update feed is published yet, so <see cref="AppUpdateOptions.FeedUrl"/>
/// is null and the update page labels self-updates honestly unavailable
/// until one exists.
/// </summary>
public sealed class VelopackAppUpdater : IAppUpdater
{
    private const string DevBuildMessage =
        "This copy of Ataraxia wasn't installed via Velopack (dev/debug build).";

    private readonly string? _feedUrl;
    private UpdateInfo? _lastCheck;

    public VelopackAppUpdater(AppUpdateOptions options)
    {
        _feedUrl = string.IsNullOrWhiteSpace(options.FeedUrl) ? null : options.FeedUrl;
    }

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public bool IsFeedConfigured => _feedUrl is not null;

    public async Task<AppUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var manager = Manager();
        try
        {
            _lastCheck = await manager.CheckForUpdatesAsync();
        }
        catch (NotInstalledException ex)
        {
            throw new AppNotInstalledException(DevBuildMessage, ex);
        }

        return _lastCheck is null
            ? null
            : new AppUpdateInfo(_lastCheck.TargetFullRelease.Version.ToString());
    }

    public async Task DownloadAsync(
        AppUpdateInfo update,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        var check = MatchingCheck(update);
        var manager = Manager();
        try
        {
            await manager.DownloadUpdatesAsync(check, p => progress.Report(p / 100.0), cancellationToken);
        }
        catch (NotInstalledException ex)
        {
            throw new AppNotInstalledException(DevBuildMessage, ex);
        }
    }

    public void ApplyAndRestart(AppUpdateInfo update)
    {
        Manager().ApplyUpdatesAndRestart(MatchingCheck(update).TargetFullRelease);
    }

    private UpdateInfo MatchingCheck(AppUpdateInfo update)
    {
        if (_lastCheck is null
            || !string.Equals(_lastCheck.TargetFullRelease.Version.ToString(), update.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Check for updates before downloading or applying.");
        }

        return _lastCheck;
    }

    private UpdateManager Manager() =>
        _feedUrl is null
            ? throw new InvalidOperationException("No update feed is configured.")
            : new UpdateManager(_feedUrl);
}
