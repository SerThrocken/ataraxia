using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Global-hotkey binding parsing/serialization. The binding is the only
/// piece of the hotkey feature that is pure logic, so it carries the
/// unit tests; the Win32 RegisterHotKey call itself is integration-only.
/// </summary>
public sealed class HotkeyBindingTests
{
    [Fact]
    public void Parse_CtrlShiftA_YieldsExpectedBinding()
    {
        Assert.True(HotkeyBinding.TryParse("Ctrl+Shift+A", out var binding));

        Assert.NotNull(binding);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, binding!.Modifiers);
        Assert.Equal(0x41u, binding.VirtualKey);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        Assert.True(HotkeyBinding.TryParse("ctrl+shift+a", out var binding));

        Assert.NotNull(binding);
        Assert.Equal(HotkeyBinding.Default, binding);
    }

    [Fact]
    public void Parse_ControlAlias_MapsToControl()
    {
        Assert.True(HotkeyBinding.TryParse("Control+Alt+Delete", out var binding));

        Assert.NotNull(binding);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, binding!.Modifiers);
        Assert.Equal(0x2Eu, binding.VirtualKey);
    }

    [Fact]
    public void Parse_WinF12_YieldsFunctionKey()
    {
        Assert.True(HotkeyBinding.TryParse("Win+F12", out var binding));

        Assert.NotNull(binding);
        Assert.Equal(HotkeyModifiers.Win, binding!.Modifiers);
        Assert.Equal(0x7Bu, binding.VirtualKey); // VK_F12
    }

    [Fact]
    public void Parse_DigitKey_MapsToAscii()
    {
        Assert.True(HotkeyBinding.TryParse("Alt+1", out var binding));

        Assert.NotNull(binding);
        Assert.Equal(0x31u, binding!.VirtualKey); // '1'
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]            // bare key, no modifier — refused by policy
    [InlineData("Ctrl+")]        // missing key
    [InlineData("+A")]           // missing modifier
    [InlineData("Ctrl+Shift")]   // no key at all
    [InlineData("Ctrl+A+B")]     // two keys
    [InlineData("Ctrl+Bogus")]   // unknown key
    [InlineData("Bogus+A")]      // unknown modifier
    public void Parse_InvalidInput_ReturnsFalse(string? text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out var binding));
        Assert.Null(binding);
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        Assert.True(HotkeyBinding.TryParse("Shift+Alt+F5", out var binding));
        Assert.NotNull(binding);

        Assert.True(HotkeyBinding.TryParse(binding!.ToString(), out var reparsed));
        Assert.Equal(binding, reparsed);
    }

    [Fact]
    public void ToString_UsesCanonicalModifierOrder()
    {
        // Input order is normalized: Ctrl, Alt, Shift, Win.
        Assert.True(HotkeyBinding.TryParse("Win+Shift+Ctrl+Alt+X", out var binding));
        Assert.Equal("Ctrl+Alt+Shift+Win+X", binding!.ToString());
    }

    [Fact]
    public void ToRegisterHotKeyArgs_MatchesWin32ModValues()
    {
        // MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8 by design.
        var (modifiers, vk) = HotkeyBinding.Default.ToRegisterHotKeyArgs();

        Assert.Equal(2u | 4u, modifiers);
        Assert.Equal(0x41u, vk);
    }

    [Fact]
    public void Default_IsCtrlShiftA()
    {
        Assert.Equal("Ctrl+Shift+A", HotkeyBinding.Default.ToString());
    }
}
