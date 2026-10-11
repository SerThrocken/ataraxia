using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class MarkdownParserTests
{
    [Fact]
    public void HeadingParses()
    {
        var blocks = MarkdownParser.Parse("# Hello");

        var h = Assert.IsType<MdHeading>(Assert.Single(blocks));
        Assert.Equal(1, h.Level);
        Assert.Equal("Hello", Assert.IsType<MdText>(Assert.Single(h.Inlines)).Text);
    }

    [Fact]
    public void BoldCodeAndLinkInlinesParse()
    {
        var blocks = MarkdownParser.Parse("**bold** and `code` and [text](https://example.com)");

        var p = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.IsType<MdStrong>(p.Inlines[0]);
        Assert.IsType<MdCodeSpan>(p.Inlines[2]);
        var link = Assert.IsType<MdLink>(p.Inlines[4]);
        Assert.Equal("https://example.com", link.Url);
    }

    [Fact]
    public void FencedCodeBlockKeepsLanguage()
    {
        var blocks = MarkdownParser.Parse("```csharp\nvar x = 1;\n```");

        var code = Assert.IsType<MdCodeBlock>(Assert.Single(blocks));
        Assert.Equal("csharp", code.Language);
        Assert.Contains("var x = 1;", code.Code);
    }

    [Fact]
    public void BulletListParses()
    {
        var blocks = MarkdownParser.Parse("- a\n- b\n");

        var list = Assert.IsType<MdList>(Assert.Single(blocks));
        Assert.False(list.Ordered);
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void QuoteParses()
    {
        var blocks = MarkdownParser.Parse("> quoted text");

        var quote = Assert.IsType<MdQuote>(Assert.Single(blocks));
        Assert.NotEmpty(quote.Blocks);
    }

    [Fact]
    public void EmptyInputYieldsNoBlocks() =>
        Assert.Empty(MarkdownParser.Parse(""));
}
