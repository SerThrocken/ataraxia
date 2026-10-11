namespace Hermes.Client.Infrastructure;

/// <summary>
/// On-disk locations of the Electron Hermes desktop app's state.
/// Injectable so imports can be tested and dry-run against any tree.
/// </summary>
public sealed record ElectronDataPaths(
    string ElectronAppDataDir,
    string HermesLocalDir)
{
    /// <summary>
    /// %APPDATA%\Hermes and %LOCALAPPDATA%\hermes for the current user.
    /// </summary>
    public static ElectronDataPaths Default => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Hermes"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "hermes"));
}
