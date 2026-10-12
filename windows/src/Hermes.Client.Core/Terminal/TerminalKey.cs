// Interactive PTY support: platform backend contract, VT key encoding,
// screen buffer + ANSI parser, and the session that ties them together.
// All UI-free: unit-testable on any OS with a fake backend.

using System;

namespace Hermes.Client.Core.Terminal;

/// <summary>
/// UI-free description of a key gesture for terminal input.
/// The WinUI view maps <c>KeyRoutedEventArgs</c> onto this; the encoder
/// turns it into VT bytes. Kept UI-free so it unit-tests anywhere.
/// </summary>
public enum TerminalKey
{
    None,
    Character,
    Enter,
    Backspace,
    Tab,
    Escape,
    Delete,
    Insert,
    Home,
    End,
    PageUp,
    PageDown,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
}

[Flags]
public enum TerminalModifiers : byte
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}

/// <summary>
/// A single key gesture. <see cref="Character"/> is only meaningful when
/// <see cref="Key"/> is <see cref="TerminalKey.Character"/>.
/// </summary>
public readonly record struct TerminalKeyPress(
    TerminalKey Key,
    TerminalModifiers Modifiers = TerminalModifiers.None,
    char Character = '\0');
