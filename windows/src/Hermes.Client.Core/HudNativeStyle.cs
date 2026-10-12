namespace Hermes.Client.Core;

/// <summary>
/// Win32 extended-style constants and pure flag-combination helpers for the
/// companion-HUD window. The actual <c>SetWindowLongPtr</c> call lives in the
/// WinUI3 window (one line, untestable off Windows); the flag math here is
/// unit-tested, so a regression in the "never steal focus" behavior fails a
/// test instead of shipping silently.
/// </summary>
public static class HudNativeStyle
{
    // GWL_EXSTYLE
    public const int WindowLongExStyle = -20;

    // WS_EX_* values (winuser.h)
    public const uint NoActivate = 0x08000000;   // WS_EX_NOACTIVATE
    public const uint Transparent = 0x00000020;  // WS_EX_TRANSPARENT
    public const uint Layered = 0x00002000;      // WS_EX_LAYERED
    public const uint ToolWindow = 0x00000080;   // WS_EX_TOOLWINDOW

    // ShowWindow commands
    public const uint ShowNoActivate = 8;        // SW_SHOWNA

    // SetLayeredWindowAttributes flags
    public const uint LayeredAlpha = 0x2;        // LWA_ALPHA

    /// <summary>
    /// The non-negotiable one: the HUD must never take activation (focus),
    /// no matter what flags it already carries.
    /// </summary>
    public static uint WithNoActivate(uint exStyle) => exStyle | NoActivate;

    public static bool HasNoActivate(uint exStyle) => (exStyle & NoActivate) != 0;

    /// <summary>Layered is required for per-window alpha (the opacity setting).</summary>
    public static uint WithLayered(uint exStyle) => exStyle | Layered;

    /// <summary>
    /// Tool windows take no taskbar button and no Alt+Tab entry — the
    /// overlay stays out of the way entirely.
    /// </summary>
    public static uint WithToolWindow(uint exStyle) => exStyle | ToolWindow;

    public static bool HasToolWindow(uint exStyle) => (exStyle & ToolWindow) != 0;

    /// <summary>
    /// Click-through toggles hit-test transparency only; the layered flag
    /// (opacity) is left exactly as it was.
    /// </summary>
    public static uint WithClickThrough(uint exStyle, bool clickThrough) =>
        clickThrough ? exStyle | Transparent : exStyle & ~Transparent;

    public static bool HasClickThrough(uint exStyle) => (exStyle & Transparent) != 0;

    /// <summary>Opacity 0.35–1.0 → alpha byte for LWA_ALPHA.</summary>
    public static byte OpacityToAlpha(double opacity) =>
        (byte)Math.Round(HudSettings.ClampOpacity(opacity) * 255);
}
