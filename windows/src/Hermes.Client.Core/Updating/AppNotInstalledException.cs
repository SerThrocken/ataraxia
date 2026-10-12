namespace Hermes.Client.Core;

/// <summary>
/// Thrown by an <see cref="IAppUpdater"/> when the app is not installed via
/// the updater (e.g. a dev/debug build run from the build output). The
/// Velopack backend translates Velopack's <c>NotInstalledException</c> into
/// this so Core stays free of the Velopack SDK.
/// </summary>
public sealed class AppNotInstalledException : Exception
{
    public AppNotInstalledException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
