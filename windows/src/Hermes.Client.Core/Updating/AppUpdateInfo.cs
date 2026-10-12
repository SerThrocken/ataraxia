namespace Hermes.Client.Core;

/// <summary>
/// A newer client version found by an <see cref="IAppUpdater"/>.
/// </summary>
public sealed record AppUpdateInfo(string Version);
