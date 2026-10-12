using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// The never-steal-focus guard, as unit-testable math:
/// <see cref="HudNativeStyle"/> must always be able to force
/// WS_EX_NOACTIVATE on, must never drop it, and must toggle click-through
/// without touching the layered/opacity flag.
/// </summary>
public sealed class HudNativeStyleTests
{
    [Fact]
    public void WithNoActivateAlwaysSetsTheFlag()
    {
        Assert.True(HudNativeStyle.HasNoActivate(HudNativeStyle.WithNoActivate(0)));
        Assert.True(HudNativeStyle.HasNoActivate(
            HudNativeStyle.WithNoActivate(0x00040000))); // some other ex-style bits
    }

    [Fact]
    public void WithNoActivatePreservesExistingFlags()
    {
        var combined = HudNativeStyle.WithNoActivate(
            HudNativeStyle.Layered | HudNativeStyle.Transparent);

        Assert.True(HudNativeStyle.HasNoActivate(combined));
        Assert.NotEqual(0u, combined & HudNativeStyle.Layered);
        Assert.NotEqual(0u, combined & HudNativeStyle.Transparent);
    }

    [Fact]
    public void HasNoActivateIsFalseByDefault()
    {
        Assert.False(HudNativeStyle.HasNoActivate(0));
    }

    [Fact]
    public void ClickThroughToggleOnlyTouchesTheTransparentFlag()
    {
        var on = HudNativeStyle.WithClickThrough(
            HudNativeStyle.Layered | HudNativeStyle.NoActivate, clickThrough: true);

        Assert.True(HudNativeStyle.HasClickThrough(on));
        Assert.True(HudNativeStyle.HasNoActivate(on));
        Assert.NotEqual(0u, on & HudNativeStyle.Layered);

        var off = HudNativeStyle.WithClickThrough(on, clickThrough: false);

        Assert.False(HudNativeStyle.HasClickThrough(off));
        Assert.True(HudNativeStyle.HasNoActivate(off)); // never dropped
        Assert.NotEqual(0u, off & HudNativeStyle.Layered);
    }

    [Fact]
    public void WithToolWindowSetsTheFlagAndPreservesOthers()
    {
        var combined = HudNativeStyle.WithToolWindow(
            HudNativeStyle.NoActivate | HudNativeStyle.Layered);

        Assert.True(HudNativeStyle.HasToolWindow(combined));
        Assert.True(HudNativeStyle.HasNoActivate(combined));
        Assert.NotEqual(0u, combined & HudNativeStyle.Layered);
        Assert.False(HudNativeStyle.HasToolWindow(0));
    }

    [Theory]
    [InlineData(1.0, 255)]
    [InlineData(0.92, 235)]
    [InlineData(0.5, 128)]
    [InlineData(0.35, 89)]
    [InlineData(0.0, 89)]   // clamped to MinOpacity
    [InlineData(2.0, 255)]  // clamped to MaxOpacity
    [InlineData(double.NaN, 235)] // falls back to the default opacity
    public void OpacityToAlphaClampsAndScales(double opacity, int expected)
    {
        Assert.Equal(expected, (int)HudNativeStyle.OpacityToAlpha(opacity));
    }
}
