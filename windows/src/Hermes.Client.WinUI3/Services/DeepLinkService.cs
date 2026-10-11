using Microsoft.Win32;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// Registers and handles the <c>ataraxia://</c> protocol so outside tools
/// (the PowerToys Run plugin, scripts) can open the app at a session:
/// <c>ataraxia://session/&lt;id&gt;</c>. Registration is per-user
/// (<c>HKCU\Software\Classes</c>), idempotent, and best-effort — the app
/// works normally when registration is unavailable.
/// </summary>
public static class DeepLinkService
{
    public const string Scheme = "ataraxia";

    public static void EnsureProtocolRegistered()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                return;
            }

            var command = $"\"{exe}\" \"%1\"";
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            if (key is null)
            {
                return;
            }

            var currentCommand = key.OpenSubKey(@"shell\open\command")?.GetValue("") as string;
            if (string.Equals(key.GetValue("") as string, "URL:Ataraxia Protocol", StringComparison.Ordinal)
                && string.Equals(currentCommand, command, StringComparison.OrdinalIgnoreCase))
            {
                return; // Already registered correctly.
            }

            key.SetValue("", "URL:Ataraxia Protocol");
            key.SetValue("URL Protocol", "");
            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey?.SetValue("", command);
        }
        catch
        {
            // Best-effort: protocol registration must never break launch.
        }
    }

    /// <summary>
    /// Handles a protocol activation from <c>App.OnLaunched</c>: navigates to
    /// the session when the URI is <c>ataraxia://session/{id}</c>.
    /// </summary>
    public static void HandleLaunchActivation(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            var args = AppInstance.GetActivatedEventArgs();
            if (args.Kind != ExtendedActivationKind.Protocol
                || args.Data is not ProtocolActivatedEventArgs protocol)
            {
                return;
            }

            if (TryParseSessionId(protocol.Uri) is not { } sessionId)
            {
                return;
            }

            window.DispatcherQueue.TryEnqueue(() => MainWindow.NavigateToChat(sessionId, null));
        }
        catch
        {
            // A malformed activation must never break launch.
        }
    }

    /// <summary>
    /// Parses <c>ataraxia://session/{id}</c>. Returns the session id, or null
    /// for anything else (including a bare <c>ataraxia://</c>).
    /// </summary>
    public static string? TryParseSessionId(Uri uri)
    {
        if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!uri.Host.Equals("session", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
