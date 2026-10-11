using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Hermes.Client.Core;

// UI-agnostic Markdown document model. Parsed from Markdig's AST by
// MarkdownParser below (original mapping code); rendered to XAML by the
// WinUI3 project's MarkdownXaml builder.

public abstract record MdBlock;

public sealed record MdHeading(int Level, IReadOnlyList<MdInline> Inlines) : MdBlock;

public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock;

public sealed record MdCodeBlock(string? Language, string Code) : MdBlock;

public sealed record MdQuote(IReadOnlyList<MdBlock> Blocks) : MdBlock;

public sealed record MdList(bool Ordered, IReadOnlyList<IReadOnlyList<MdBlock>> Items) : MdBlock;

public sealed record MdThematicBreak : MdBlock;

public sealed record MdTable(IReadOnlyList<IReadOnlyList<string>> Rows) : MdBlock;

public abstract record MdInline;

public sealed record MdText(string Text) : MdInline;

public sealed record MdEmphasis(IReadOnlyList<MdInline> Children) : MdInline;

public sealed record MdStrong(IReadOnlyList<MdInline> Children) : MdInline;

public sealed record MdStrikethrough(IReadOnlyList<MdInline> Children) : MdInline;

public sealed record MdCodeSpan(string Code) : MdInline;

public sealed record MdLink(IReadOnlyList<MdInline> Children, string? Url) : MdInline;

public sealed record MdLineBreak : MdInline;

/// <summary>Parses Markdown into the <see cref="MdBlock"/> model via Markdig.</summary>
public static class MarkdownParser
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        return MapBlocks(document);
    }

    private static IReadOnlyList<MdBlock> MapBlocks(ContainerBlock container)
    {
        var blocks = new List<MdBlock>();
        foreach (var block in container)
        {
            var mapped = MapBlock(block);
            if (mapped is not null)
            {
                blocks.Add(mapped);
            }
        }

        return blocks;
    }

    private static MdBlock? MapBlock(Block block) => block switch
    {
        HeadingBlock heading => new MdHeading(
            Math.Clamp(heading.Level, 1, 6), MapInlines(heading.Inline)),
        ParagraphBlock paragraph => new MdParagraph(MapInlines(paragraph.Inline)),
        FencedCodeBlock fenced => new MdCodeBlock(
            string.IsNullOrWhiteSpace(fenced.Info) ? null : fenced.Info!.Trim().Split(' ')[0],
            fenced.Lines.ToString()),
        CodeBlock code => new MdCodeBlock(null, code.Lines.ToString()),
        QuoteBlock quote => new MdQuote(MapBlocks(quote)),
        ListBlock list => new MdList(
            list.IsOrdered,
            list.OfType<ListItemBlock>().Select(item => MapBlocks(item)).ToList()),
        ThematicBreakBlock => new MdThematicBreak(),
        Table table => new MdTable(
            table.OfType<TableRow>().Select(row => (IReadOnlyList<string>)row
                .OfType<TableCell>()
                .Select(cell => cell.OfType<ParagraphBlock>()
                    .Select(p => string.Concat(p.Inline?.Select(InlineText) ?? []))
                    .FirstOrDefault() ?? "")
                .ToList()).ToList()),
        HtmlBlock html => new MdParagraph([new MdText(html.Lines.ToString().Trim())]),
        _ => null, // Unknown block kinds are skipped rather than misrendered.
    };

    private static IReadOnlyList<MdInline> MapInlines(ContainerInline? inline)
    {
        var inlines = new List<MdInline>();
        for (var child = inline?.FirstChild; child is not null; child = child.NextSibling)
        {
            var mapped = MapInline(child);
            if (mapped is not null)
            {
                inlines.Add(mapped);
            }
        }

        return inlines;
    }

    private static MdInline? MapInline(Inline inline) => inline switch
    {
        LiteralInline literal => new MdText(literal.Content.ToString()),
        LineBreakInline => new MdLineBreak(),
        CodeInline code => new MdCodeSpan(code.Content),
        AutolinkInline autolink => new MdLink([new MdText(autolink.Url)], autolink.Url),
        LinkInline link => new MdLink(MapInlines(link), link.Url),
        EmphasisInline emphasis when emphasis.DelimiterChar is '~' =>
            new MdStrikethrough(MapInlines(emphasis)),
        EmphasisInline emphasis when emphasis.DelimiterCount >= 2 =>
            new MdStrong(MapInlines(emphasis)),
        EmphasisInline emphasis => new MdEmphasis(MapInlines(emphasis)),
        HtmlInline html => new MdText(html.Tag),
        _ => new MdText(InlineText(inline)),
    };

    private static string InlineText(Inline inline) => inline switch
    {
        LiteralInline literal => literal.Content.ToString(),
        CodeInline code => code.Content,
        LinkInline link => string.Concat(link.Select(InlineText)),
        ContainerInline container => string.Concat(container.Select(InlineText)),
        _ => "",
    };
}
