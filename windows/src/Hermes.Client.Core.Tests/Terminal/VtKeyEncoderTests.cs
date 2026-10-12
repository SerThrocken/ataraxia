using System.Text;
using Hermes.Client.Core.Terminal;
using Xunit;

namespace Hermes.Client.Core.Tests.Terminal;

/// <summary>
/// VT key encoding (<c>VtKeyEncoder</c>): every gesture maps to the exact
/// byte sequence a ConPTY child expects. Ctrl+C must be 0x03 — that is the
/// whole point of the interrupt path.
/// </summary>
public sealed class VtKeyEncoderTests
{
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes);

    [Fact]
    public void CtrlC_EncodesAsEtx()
    {
        Assert.Equal([0x03], VtKeyEncoder.Encode(new(TerminalKey.Character, TerminalModifiers.Control, 'c')));
        Assert.Equal([0x03], VtKeyEncoder.Encode(new(TerminalKey.Character, TerminalModifiers.Control, 'C')));
    }

    [Theory]
    [InlineData('a', 0x01)]
    [InlineData('z', 0x1A)]
    [InlineData('d', 0x04)] // Ctrl+D: EOF
    [InlineData(' ', 0x00)]
    [InlineData('[', 0x1B)]
    [InlineData('\\', 0x1C)]
    [InlineData(']', 0x1D)]
    public void CtrlLetter_EncodesAsControlCode(char c, byte expected)
    {
        Assert.Equal([expected], VtKeyEncoder.Encode(new(TerminalKey.Character, TerminalModifiers.Control, c)));
    }

    [Fact]
    public void Enter_EncodesAsCarriageReturn()
    {
        Assert.Equal([0x0D], VtKeyEncoder.Encode(new(TerminalKey.Enter)));
    }

    [Fact]
    public void Backspace_EncodesAsDel_ByDefault()
    {
        Assert.Equal([0x7F], VtKeyEncoder.Encode(new(TerminalKey.Backspace)));
    }

    [Fact]
    public void Tab_EncodesAsTab_ShiftTab_AsCsiZ()
    {
        Assert.Equal([0x09], VtKeyEncoder.Encode(new(TerminalKey.Tab)));
        Assert.Equal("\x1b[Z", Encoding.ASCII.GetString(
            VtKeyEncoder.Encode(new(TerminalKey.Tab, TerminalModifiers.Shift))));
    }

    [Fact]
    public void Escape_EncodesAsEsc()
    {
        Assert.Equal([0x1B], VtKeyEncoder.Encode(new(TerminalKey.Escape)));
    }

    [Theory]
    [InlineData(TerminalKey.ArrowUp, "\x1b[A")]
    [InlineData(TerminalKey.ArrowDown, "\x1b[B")]
    [InlineData(TerminalKey.ArrowRight, "\x1b[C")]
    [InlineData(TerminalKey.ArrowLeft, "\x1b[D")]
    [InlineData(TerminalKey.Home, "\x1b[H")]
    [InlineData(TerminalKey.End, "\x1b[F")]
    [InlineData(TerminalKey.Insert, "\x1b[2~")]
    [InlineData(TerminalKey.Delete, "\x1b[3~")]
    [InlineData(TerminalKey.PageUp, "\x1b[5~")]
    [InlineData(TerminalKey.PageDown, "\x1b[6~")]
    public void NavigationKeys_EncodeAsCsi(TerminalKey key, string expected)
    {
        Assert.Equal(expected, Encoding.ASCII.GetString(VtKeyEncoder.Encode(new(key))));
    }

    [Theory]
    [InlineData(TerminalKey.ArrowUp, TerminalModifiers.Control, "\x1b[1;5A")]
    [InlineData(TerminalKey.ArrowLeft, TerminalModifiers.Shift, "\x1b[1;2D")]
    [InlineData(TerminalKey.ArrowRight, TerminalModifiers.Alt, "\x1b[1;3C")]
    [InlineData(TerminalKey.Home, TerminalModifiers.Control, "\x1b[1;5H")]
    [InlineData(TerminalKey.Delete, TerminalModifiers.Shift, "\x1b[3;2~")]
    public void ModifiedNavigationKeys_CarryModifierParam(TerminalKey key, TerminalModifiers mods, string expected)
    {
        Assert.Equal(expected, Encoding.ASCII.GetString(VtKeyEncoder.Encode(new(key, mods))));
    }

    [Theory]
    [InlineData(TerminalKey.F1, "\x1bOP")]
    [InlineData(TerminalKey.F2, "\x1bOQ")]
    [InlineData(TerminalKey.F3, "\x1bOR")]
    [InlineData(TerminalKey.F4, "\x1bOS")]
    [InlineData(TerminalKey.F5, "\x1b[15~")]
    [InlineData(TerminalKey.F6, "\x1b[17~")]
    [InlineData(TerminalKey.F7, "\x1b[18~")]
    [InlineData(TerminalKey.F8, "\x1b[19~")]
    [InlineData(TerminalKey.F9, "\x1b[20~")]
    [InlineData(TerminalKey.F10, "\x1b[21~")]
    [InlineData(TerminalKey.F11, "\x1b[23~")]
    [InlineData(TerminalKey.F12, "\x1b[24~")]
    public void FunctionKeys_EncodeAsSs3OrCsi(TerminalKey key, string expected)
    {
        Assert.Equal(expected, Encoding.ASCII.GetString(VtKeyEncoder.Encode(new(key))));
    }

    [Fact]
    public void ShiftF5_CarriesModifierParam()
    {
        Assert.Equal("\x1b[15;2~", Encoding.ASCII.GetString(
            VtKeyEncoder.Encode(new(TerminalKey.F5, TerminalModifiers.Shift))));
    }

    [Fact]
    public void AltX_PrefixesEsc()
    {
        Assert.Equal("\x1bx", Encoding.ASCII.GetString(
            VtKeyEncoder.Encode(new(TerminalKey.Character, TerminalModifiers.Alt, 'x'))));
    }

    [Fact]
    public void PlainCharacter_EncodesAsUtf8()
    {
        Assert.Equal("é", Encoding.UTF8.GetString(
            VtKeyEncoder.Encode(new(TerminalKey.Character, TerminalModifiers.None, 'é'))));
    }

    [Fact]
    public void UnknownKey_EncodesAsEmpty()
    {
        Assert.Empty(VtKeyEncoder.Encode(new(TerminalKey.None)));
    }

    [Fact]
    public void CtrlWithUnhandledChar_PassesThrough()
    {
        // Ctrl+1 has no control code: the character goes through unchanged.
        Assert.Equal("1", Encoding.ASCII.GetString(
            VtKeyEncoder.Encode(new(TerminalKey.Character, TerminalModifiers.Control, '1'))));
    }
}
