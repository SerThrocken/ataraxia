namespace Hermes.Client.Core;

/// <summary>
/// System-wide hotkey registration (Win32 RegisterHotKey behind this
/// boundary; the WinUI3 implementation owns the hidden message window).
/// Kept as an interface so Settings can report conflicts and tests can
/// substitute a fake; the only pure-logic part (modifier/VK mapping)
/// lives on <see cref="HotkeyBinding"/>.
/// </summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>Raised on the UI thread when the registered hotkey fires.</summary>
    event EventHandler? Pressed;

    /// <summary>
    /// Registers <paramref name="binding"/> system-wide. Returns false with
    /// a human-readable <paramref name="error"/> (e.g. already taken by
    /// another app) instead of throwing — a conflict is a settings problem,
    /// not a crash.
    /// </summary>
    bool TryRegister(HotkeyBinding binding, out string? error);

    /// <summary>Releases the registration, if any.</summary>
    void Unregister();
}
