namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// Tray + global-hotkey settings blob (name "tray", via ISettingsService).
/// The API key never lives here — see SettingsViewModel for that split.
/// </summary>
public sealed record TraySettings
{
    /// <summary>Global quick-entry hotkey, e.g. "Ctrl+Shift+A".</summary>
    public string Hotkey { get; init; } = "Ctrl+Shift+A";

    /// <summary>Whether the Win11 "hidden tray icons" first-run hint was shown.</summary>
    public bool TrayHintShown { get; init; }
}
