using System;
using System.Text;

namespace Hermes.Client.Core.Terminal;

/// <summary>
/// Encodes <see cref="TerminalKeyPress"/> gestures to the VT byte sequences
/// a ConPTY child expects (xterm conventions). Ctrl+C becomes 0x03, arrows
/// become CSI sequences, F-keys become SS3/CSI sequences, Alt prefixes ESC.
/// Pure function: no I/O, fully unit-testable.
/// </summary>
public static class VtKeyEncoder
{
    private const byte Esc = 0x1B;

    public static byte[] Encode(TerminalKeyPress press)
    {
        var mods = press.Modifiers;
        return press.Key switch
        {
            TerminalKey.Character => EncodeCharacter(press.Character, mods),
            TerminalKey.Enter => [(byte)'\r'],
            TerminalKey.Backspace => mods.HasFlag(TerminalModifiers.Control)
                ? [(byte)'\b']
                : [(byte)0x7F], // xterm default: DEL
            TerminalKey.Tab => mods.HasFlag(TerminalModifiers.Shift)
                ? Ascii("\x1b[Z")
                : [(byte)'\t'],
            TerminalKey.Escape => [Esc],
            TerminalKey.Delete => CsiTilde("3", mods),
            TerminalKey.Insert => CsiTilde("2", mods),
            TerminalKey.Home => mods == TerminalModifiers.None ? Ascii("\x1b[H") : CsiMod("H", mods),
            TerminalKey.End => mods == TerminalModifiers.None ? Ascii("\x1b[F") : CsiMod("F", mods),
            TerminalKey.PageUp => CsiTilde("5", mods),
            TerminalKey.PageDown => CsiTilde("6", mods),
            TerminalKey.ArrowUp => Arrow('A', mods),
            TerminalKey.ArrowDown => Arrow('B', mods),
            TerminalKey.ArrowRight => Arrow('C', mods),
            TerminalKey.ArrowLeft => Arrow('D', mods),
            TerminalKey.F1 => FunctionKey("P", 1, mods),
            TerminalKey.F2 => FunctionKey("Q", 1, mods),
            TerminalKey.F3 => FunctionKey("R", 1, mods),
            TerminalKey.F4 => FunctionKey("S", 1, mods),
            TerminalKey.F5 => FunctionKey("15", 2, mods),
            TerminalKey.F6 => FunctionKey("17", 2, mods),
            TerminalKey.F7 => FunctionKey("18", 2, mods),
            TerminalKey.F8 => FunctionKey("19", 2, mods),
            TerminalKey.F9 => FunctionKey("20", 2, mods),
            TerminalKey.F10 => FunctionKey("21", 2, mods),
            TerminalKey.F11 => FunctionKey("23", 2, mods),
            TerminalKey.F12 => FunctionKey("24", 2, mods),
            _ => [],
        };
    }

    private static byte[] EncodeCharacter(char c, TerminalModifiers mods)
    {
        var ctrl = mods.HasFlag(TerminalModifiers.Control);
        var alt = mods.HasFlag(TerminalModifiers.Alt);

        if (ctrl && !alt)
        {
            // Ctrl+letter -> 0x01..0x1A (Ctrl+C is 0x03); a few punctuation
            // forms have standard control codes too.
            var upper = char.ToUpperInvariant(c);
            if (upper is >= 'A' and <= 'Z')
            {
                return [(byte)(upper - 'A' + 1)];
            }

            var code = c switch
            {
                ' ' or '@' => 0x00,
                '[' => Esc,
                '\\' => 0x1C,
                ']' => 0x1D,
                '^' => 0x1E,
                '_' or '/' => 0x1F,
                '?' => 0x7F,
                _ => -1,
            };
            if (code >= 0)
            {
                return [(byte)code];
            }
            // No control code for this key: pass the character through.
            return Encoding.UTF8.GetBytes([c]);
        }

        var bytes = Encoding.UTF8.GetBytes([c]);
        if (!alt)
        {
            return bytes;
        }

        // Alt (Meta): ESC prefix. With Ctrl held too, prefix the control code.
        if (ctrl)
        {
            var inner = EncodeCharacter(c, TerminalModifiers.Control);
            var prefixed = new byte[inner.Length + 1];
            prefixed[0] = Esc;
            inner.CopyTo(prefixed, 1);
            return prefixed;
        }

        var result = new byte[bytes.Length + 1];
        result[0] = Esc;
        bytes.CopyTo(result, 1);
        return result;
    }

    /// <summary>
    /// xterm modifier parameter: 1 + shift*1 + alt*2 + ctrl*4.
    /// </summary>
    private static int ModifierParam(TerminalModifiers mods)
    {
        var m = 1;
        if (mods.HasFlag(TerminalModifiers.Shift))
        {
            m += 1;
        }

        if (mods.HasFlag(TerminalModifiers.Alt))
        {
            m += 2;
        }

        if (mods.HasFlag(TerminalModifiers.Control))
        {
            m += 4;
        }

        return m;
    }

    private static byte[] Arrow(char dir, TerminalModifiers mods) =>
        mods == TerminalModifiers.None
            ? Ascii($"\x1b[{dir}")
            : Ascii($"\x1b[1;{ModifierParam(mods)}{dir}");

    private static byte[] CsiTilde(string n, TerminalModifiers mods) =>
        mods == TerminalModifiers.None
            ? Ascii($"\x1b[{n}~")
            : Ascii($"\x1b[{n};{ModifierParam(mods)}~");

    private static byte[] CsiMod(string final, TerminalModifiers mods) =>
        Ascii($"\x1b[1;{ModifierParam(mods)}{final}");

    private static byte[] FunctionKey(string code, int kind, TerminalModifiers mods)
    {
        // kind 1: SS3 (F1-F4); kind 2: CSI number ~ (F5-F12).
        if (mods == TerminalModifiers.None)
        {
            return kind == 1 ? Ascii($"\x1bO{code}") : Ascii($"\x1b[{code}~");
        }

        return kind == 1
            ? Ascii($"\x1b[1;{ModifierParam(mods)}{code}")
            : Ascii($"\x1b[{code};{ModifierParam(mods)}~");
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
}
