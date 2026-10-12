namespace Hermes.Client.Core;

/// <summary>
/// Companion-HUD settings blob (name "hud", via <see cref="ISettingsService"/>).
/// Opt-in by design: <see cref="Enabled"/> defaults to false, so the overlay
/// never pops up uninvited. Position is <see cref="double?"/> (null = let the
/// window pick its default spot) because the JSON settings store uses strict
/// number handling — NaN would not survive a save/load round trip.
/// </summary>
public sealed record HudSettings
{
    public const double DefaultOpacity = 0.92;
    public const double MinOpacity = 0.35;
    public const double MaxOpacity = 1.0;

    /// <summary>Master switch. False on first run — the HUD starts hidden.</summary>
    public bool Enabled { get; init; }

    /// <summary>Window opacity, clamped to <see cref="MinOpacity"/>…<see cref="MaxOpacity"/>.</summary>
    public double Opacity { get; init; } = DefaultOpacity;

    /// <summary>Remembered window position (device pixels); null = default.</summary>
    public double? Left { get; init; }

    /// <summary>Remembered window position (device pixels); null = default.</summary>
    public double? Top { get; init; }

    /// <summary>When true, mouse clicks pass through the overlay.</summary>
    public bool ClickThrough { get; init; }

    /// <summary>Global toggle hotkey, e.g. "Ctrl+Shift+H". Empty = unset.</summary>
    public string Hotkey { get; init; } = string.Empty;

    public bool HasSavedPosition => Left.HasValue && Top.HasValue;

    /// <summary>Clamps <see cref="Opacity"/> into range; NaN falls back to the default.</summary>
    public HudSettings Normalized() => this with { Opacity = ClampOpacity(Opacity) };

    public static double ClampOpacity(double value) =>
        double.IsNaN(value) ? DefaultOpacity : Math.Clamp(value, MinOpacity, MaxOpacity);
}
