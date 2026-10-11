using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

// TDD for the command palette's fuzzy matcher: simple case-insensitive
// substring matching with ranking (prefix > word-boundary > early > short).
public sealed class PaletteMatcherTests
{
    [Fact]
    public void EmptyQuery_MatchesEverything_WithNeutralScore()
    {
        Assert.Equal(0, PaletteMatcher.Score("", "New session"));
        Assert.Equal(0, PaletteMatcher.Score("   ", "New session"));
        Assert.Equal(0, PaletteMatcher.Score(null, "New session"));
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        Assert.Null(PaletteMatcher.Score("xyz", "New session"));
        Assert.Null(PaletteMatcher.Score("session", ""));
        Assert.Null(PaletteMatcher.Score("session", null));
    }

    [Fact]
    public void Matching_IsCaseInsensitive_AndTrimsQuery()
    {
        Assert.NotNull(PaletteMatcher.Score("NEW", "New session"));
        Assert.NotNull(PaletteMatcher.Score("  new  ", "New session"));
        Assert.NotNull(PaletteMatcher.Score("Session", "new SESSION"));
    }

    [Fact]
    public void PrefixMatch_OutranksSubstringMatch()
    {
        var prefix = PaletteMatcher.Score("sess", "Sessions");
        var substring = PaletteMatcher.Score("sess", "Go to Sessions");
        Assert.NotNull(prefix);
        Assert.NotNull(substring);
        Assert.True(prefix > substring, $"prefix {prefix} should beat substring {substring}");
    }

    [Fact]
    public void WordBoundaryMatch_OutranksMidWordMatch()
    {
        var boundary = PaletteMatcher.Score("set", "Open Settings");
        var midWord = PaletteMatcher.Score("set", "Asset panel");
        Assert.NotNull(boundary);
        Assert.NotNull(midWord);
        Assert.True(boundary > midWord, $"boundary {boundary} should beat mid-word {midWord}");
    }

    [Fact]
    public void ShorterTarget_OutranksLongerTarget_ForSamePosition()
    {
        var shorter = PaletteMatcher.Score("new", "New");
        var longer = PaletteMatcher.Score("new", "New session details");
        Assert.NotNull(shorter);
        Assert.NotNull(longer);
        Assert.True(shorter > longer, $"shorter {shorter} should beat longer {longer}");
    }

    [Fact]
    public void EarlierMatch_OutranksLaterMatch()
    {
        var early = PaletteMatcher.Score("go", "Go to Chat");
        var late = PaletteMatcher.Score("go", "Cargo manifest");
        Assert.NotNull(early);
        Assert.NotNull(late);
        Assert.True(early > late, $"early {early} should beat late {late}");
    }
}
