using Xunit;
using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// <see cref="HudSettings"/> defaults and normalization: the HUD is opt-in
/// (never uninvited), opacity is clamped, and the position survives a
/// JSON round trip through the same strict options the settings store uses.
/// </summary>
public sealed class HudSettingsTests
{
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DefaultsAreOptInAndSane()
    {
        var settings = new HudSettings();

        Assert.False(settings.Enabled); // never pops up uninvited
        Assert.Equal(HudSettings.DefaultOpacity, settings.Opacity);
        Assert.False(settings.HasSavedPosition);
        Assert.False(settings.ClickThrough);
        Assert.Equal(string.Empty, settings.Hotkey);
    }

    [Theory]
    [InlineData(0.0, HudSettings.MinOpacity)]
    [InlineData(0.2, HudSettings.MinOpacity)]
    [InlineData(0.35, 0.35)]
    [InlineData(0.92, 0.92)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.5, HudSettings.MaxOpacity)]
    [InlineData(double.NaN, HudSettings.DefaultOpacity)]
    public void ClampOpacityBoundsTheRange(double input, double expected)
    {
        Assert.Equal(expected, HudSettings.ClampOpacity(input));
    }

    [Fact]
    public void NormalizedClampsOpacityAndKeepsEverythingElse()
    {
        var settings = new HudSettings { Enabled = true, Opacity = 5.0, ClickThrough = true }
            .Normalized();

        Assert.Equal(HudSettings.MaxOpacity, settings.Opacity);
        Assert.True(settings.Enabled);
        Assert.True(settings.ClickThrough);
    }

    [Fact]
    public void PositionRoundTripsThroughStrictJson()
    {
        var settings = new HudSettings
        {
            Enabled = true,
            Opacity = 0.8,
            Left = 1520.0,
            Top = 940.0,
            ClickThrough = true,
            Hotkey = "Ctrl+Shift+H",
        };

        var json = JsonSerializer.Serialize(settings, StrictJson);
        var loaded = JsonSerializer.Deserialize<HudSettings>(json, StrictJson);

        Assert.NotNull(loaded);
        Assert.True(loaded.Enabled);
        Assert.Equal(0.8, loaded.Opacity);
        Assert.True(loaded.HasSavedPosition);
        Assert.Equal(1520.0, loaded.Left);
        Assert.Equal(940.0, loaded.Top);
        Assert.True(loaded.ClickThrough);
        Assert.Equal("Ctrl+Shift+H", loaded.Hotkey);
    }

    [Fact]
    public void DefaultSettingsRoundTripWithoutPosition()
    {
        var json = JsonSerializer.Serialize(new HudSettings(), StrictJson);
        var loaded = JsonSerializer.Deserialize<HudSettings>(json, StrictJson);

        Assert.NotNull(loaded);
        Assert.False(loaded.HasSavedPosition);
        Assert.False(loaded.Enabled);
    }
}
