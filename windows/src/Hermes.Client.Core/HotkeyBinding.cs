namespace Hermes.Client.Core;

/// <summary>
/// Global-hotkey modifier flags. Values deliberately match the Win32
/// MOD_* constants (MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8) so
/// <see cref="HotkeyBinding.ToRegisterHotKeyArgs"/> is a cast, not a table.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A system-wide hotkey binding such as Ctrl+Shift+A: one or more
/// modifiers plus exactly one virtual key. Parsed from the Settings-page
/// text box; invalid combos are rejected, never half-registered.
/// </summary>
public sealed record HotkeyBinding(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>Default quick-entry hotkey: Ctrl+Shift+A.</summary>
    public static readonly HotkeyBinding Default = new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x41);

    /// <summary>
    /// Parses "Ctrl+Shift+A" (case-insensitive; "Control"/"Windows"/"Meta"
    /// aliases accepted). Returns false for a bare key, a missing key,
    /// more than one key, or any unknown token.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyBinding? binding)
    {
        binding = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint? vk = null;
        foreach (var raw in text.Split('+'))
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                return false;
            }

            if (TryParseModifier(token, out var mod))
            {
                modifiers |= mod;
                continue;
            }

            // Exactly one key token is allowed.
            if (vk.HasValue || !TryParseKey(token, out var key))
            {
                return false;
            }

            vk = key;
        }

        // A global hotkey without a modifier would swallow a plain keystroke.
        if (modifiers == HotkeyModifiers.None || !vk.HasValue)
        {
            return false;
        }

        binding = new HotkeyBinding(modifiers, vk.Value);
        return true;
    }

    /// <summary>
    /// Canonical display form, e.g. "Ctrl+Shift+A"; modifiers always print
    /// in Ctrl, Alt, Shift, Win order so parse-print round-trips.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyDisplayName(VirtualKey));
        return string.Join('+', parts);
    }

    /// <summary>Arguments for Win32 RegisterHotKey: (fsModifiers, vk).</summary>
    public (uint Modifiers, uint VirtualKey) ToRegisterHotKeyArgs() =>
        ((uint)Modifiers, VirtualKey);

    private static bool TryParseModifier(string token, out HotkeyModifiers modifier)
    {
        modifier = token.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => HotkeyModifiers.Control,
            "ALT" => HotkeyModifiers.Alt,
            "SHIFT" => HotkeyModifiers.Shift,
            "WIN" or "WINDOWS" or "META" => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None,
        };
        return modifier != HotkeyModifiers.None;
    }

    private static bool TryParseKey(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if ((c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9'))
            {
                vk = c; // VK for letters/digits equals the ASCII code.
                return true;
            }

            return false;
        }

        if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(token[1..], out var f) && f is >= 1 and <= 24)
        {
            vk = 0x70u + (uint)(f - 1); // VK_F1..VK_F24
            return true;
        }

        vk = token.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "INSERT" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => 0,
        };
        return vk != 0;
    }

    private static string KeyDisplayName(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(), // A-Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(), // 0-9
        >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",
        0x20 => "Space",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        _ => $"VK 0x{vk:X2}",
    };
}
