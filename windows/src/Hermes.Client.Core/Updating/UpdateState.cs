namespace Hermes.Client.Core;

/// <summary>
/// States of the <see cref="UpdateCoordinator"/> self-update flow.
/// </summary>
public enum UpdateState
{
    /// <summary>No update feed URL is configured; checks are unavailable.</summary>
    NotConfigured,

    /// <summary>Ready; no check in flight.</summary>
    Idle,

    /// <summary>A feed check is in flight.</summary>
    Checking,

    /// <summary>The feed has nothing newer than the running version.</summary>
    UpToDate,

    /// <summary>A newer version was found and is waiting to download.</summary>
    UpdateAvailable,

    /// <summary>The update package is downloading.</summary>
    Downloading,

    /// <summary>Downloaded; applying restarts the app into the new version.</summary>
    ReadyToRestart,

    /// <summary>
    /// The app is not a Velopack install (dev/debug build) — self-updates
    /// cannot work here.
    /// </summary>
    NotInstalled,

    /// <summary>A check or download failed; <see cref="UpdateCoordinator.Message"/> says why.</summary>
    Error,
}
