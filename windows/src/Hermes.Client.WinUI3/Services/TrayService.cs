using System.Drawing;
using Hermes.Client.Core;
using H.NotifyIcon.Core;

namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// System-tray icon (H.NotifyIcon, MIT — see DEPENDENCIES.md) with a flat
/// context menu and balloon notifications. Built on the framework-agnostic
/// core (TrayIconWithContextMenu): the 2.4.1 WinUI TaskbarIcon wrapper does
/// not expose a menu property (verified against the shipped 2.4.1 XML
/// docs), and the core renders the menu as a native Win32 popup — which
/// follows the Windows light/dark app theme on its own. There is no
/// ContextMenuThemeMode in 2.4.1 (it exists only on the unreleased master),
/// so no theme is forced here; that matches the brief's "System" intent.
/// </summary>
public sealed class TrayService : IDisposable
{
    /// <summary>
    /// Pinned tray Guid. Windows keys tray-icon settings (pin/visibility) to
    /// the (Guid, exe-path) pair, and the library's default Guid is a hash
    /// of the exe path — which Velopack churns on every update
    /// (%LocalAppData%\Ataraxia\app-&lt;ver&gt;). Pinning a fixed Guid keeps the
    /// registration deterministic across versions. Caveat, decided up front
    /// per the phase-3 brief: without Authenticode code-signing, Windows still
    /// resets the user's pin preference when the path changes (upstream:
    /// "the only way to keep the settings when changing the file path is to
    /// use Authenticode"). Signing is the production follow-up — until then,
    /// the first-run hint tells the user where to re-pin the icon.
    /// </summary>
    public static readonly Guid TrayGuid = Guid.Parse("379d83f3-ccf5-46f0-821b-6ca215695a5c");

    private TrayIconWithContextMenu? _icon;
    private Icon? _iconImage;
    private bool _disposed;

    /// <summary>
    /// Creates the tray icon. Must run on the UI thread; call once at launch.
    /// Pass <paramref name="toggleHud"/> to add the companion-HUD toggle item.
    /// </summary>
    public void Start(
        Action toggleWindow,
        Func<Task> newChat,
        Action missionControl,
        Action quit,
        Action? toggleHud = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icon is not null)
        {
            return;
        }

        // Flat menu: PopupMenu has no submenus by design (upstream limit), so
        // the menu stays flat — Show/Hide, New chat, Mission Control,
        // Companion HUD (Phase 4, opt-in), Quit.
        var menu = new PopupMenu();
        menu.Items.Add(new PopupMenuItem("Show/Hide window", (_, _) => toggleWindow()));
        menu.Items.Add(new PopupMenuItem("New chat", async (_, _) => await newChat()));
        menu.Items.Add(new PopupMenuItem("Mission Control", (_, _) => missionControl()));
        if (toggleHud is not null)
        {
            menu.Items.Add(new PopupMenuItem("Companion HUD", (_, _) => toggleHud()));
        }

        menu.Items.Add(new PopupMenuSeparator());
        menu.Items.Add(new PopupMenuItem("Quit Ataraxia", (_, _) => quit()));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ataraxia.ico");
        _iconImage = File.Exists(iconPath) ? new Icon(iconPath) : null;

        _icon = new TrayIconWithContextMenu(TrayGuid)
        {
            ToolTip = "Ataraxia",
            // Core takes the raw HICON handle; _iconImage is kept alive for
            // the tray lifetime and disposed with it.
            Icon = _iconImage?.Handle ?? nint.Zero,
            ContextMenu = menu,
        };

        try
        {
            _icon.Create();
        }
        catch (Exception)
        {
            // Headless/service sessions have no notification area — the tray
            // icon is a nice-to-have; the app (hotkey, toasts, window) must
            // still start. Verified: Create() throws InvalidOperationException
            // ("TryCreate failed") from a non-interactive session.
            _icon.Dispose();
            _icon = null;
        }
    }

    /// <summary>
    /// Balloon for an approval request. Wired to RunToastService's
    /// ApprovalRequested event — the same trigger point as the toast, so the
    /// two surfaces can never drift apart.
    /// </summary>
    public void ShowApprovalBalloon(string runId, ApprovalRequest request)
    {
        if (_disposed || _icon is null)
        {
            return;
        }

        var detail = request.Command ?? request.Description ?? $"Run {runId}";
        _icon.ShowNotification("Approval needed", detail, NotificationIcon.Warning);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon?.Dispose();
        _icon = null;
        _iconImage?.Dispose();
        _iconImage = null;
    }
}
