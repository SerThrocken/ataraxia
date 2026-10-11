using Hermes.Client.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Hermes.Client.WinUI3.Controls;

/// <summary>
/// Renders Markdown (parsed by <see cref="MarkdownParser"/>) as native XAML blocks.
/// All styling uses theme resources so high-contrast themes are respected.
/// </summary>
public sealed class MarkdownBlock : StackPanel
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown),
            typeof(string),
            typeof(MarkdownBlock),
            new PropertyMetadata("", OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownBlock() => Spacing = 8;

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (MarkdownBlock)d;
        self.Children.Clear();
        MarkdownXaml.AppendBlocks(self.Children, MarkdownParser.Parse((string)e.NewValue ?? ""));
    }
}

internal static class MarkdownXaml
{
    public static void AppendBlocks(UIElementCollection target, IReadOnlyList<MdBlock> blocks)
    {
        foreach (var block in blocks)
        {
            var element = ToElement(block);
            if (element is not null)
            {
                target.Add(element);
            }
        }
    }

    private static UIElement? ToElement(MdBlock block) => block switch
    {
        MdHeading h => Heading(h),
        MdParagraph p => ParagraphBlock(p.Inlines),
        MdCodeBlock c => Code(c),
        MdQuote q => Quote(q),
        MdList l => List(l),
        MdThematicBreak => new Rectangle
        {
            Height = 1,
            Fill = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Opacity = 0.4,
            Margin = new Thickness(0, 4, 0, 4),
        },
        MdTable t => Table(t),
        _ => null,
    };

    private static UIElement Heading(MdHeading heading)
    {
        var text = new RichTextBlock { IsTextSelectionEnabled = true };
        var paragraph = new Paragraph();
        AddInlines(paragraph.Inlines, heading.Inlines);
        // Headings render bold at a stepped size; theme text color is inherited.
        foreach (var inline in paragraph.Inlines.OfType<Run>())
        {
            inline.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }

        paragraph.FontSize = heading.Level switch
        {
            1 => 24,
            2 => 20,
            3 => 17,
            _ => 15,
        };
        text.Blocks.Add(paragraph);
        return text;
    }

    private static UIElement ParagraphBlock(IReadOnlyList<MdInline> inlines)
    {
        var text = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        var paragraph = new Paragraph();
        AddInlines(paragraph.Inlines, inlines);
        text.Blocks.Add(paragraph);
        return text;
    }

    private static UIElement Code(MdCodeBlock code)
    {
        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
        };
        var scroll = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        scroll.Content = new TextBlock
        {
            Text = code.Code.TrimEnd('\n'),
            FontFamily = new FontFamily("Consolas"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.NoWrap,
        };
        border.Child = scroll;
        return border;
    }

    private static UIElement Quote(MdQuote quote)
    {
        var border = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
            Opacity = 0.92,
        };
        var panel = new StackPanel { Spacing = 6 };
        AppendBlocks(panel.Children, quote.Blocks);
        border.Child = panel;
        return border;
    }

    private static UIElement List(MdList list)
    {
        var panel = new StackPanel { Spacing = 4 };
        var index = 0;
        foreach (var item in list.Items)
        {
            index++;
            var marker = list.Ordered ? $"{index}." : "•";
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var markerBlock = new TextBlock { Text = marker };
            row.Children.Add(markerBlock);
            Grid.SetColumn(markerBlock, 0);
            var body = new StackPanel { Spacing = 4 };
            AppendBlocks(body.Children, item);
            row.Children.Add(body);
            Grid.SetColumn(body, 1);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static UIElement Table(MdTable table)
    {
        if (table.Rows.Count == 0)
        {
            return new TextBlock();
        }

        var grid = new Grid
        {
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
        };
        var columns = table.Rows.Max(r => r.Count);
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var rowIndex = 0;
        foreach (var row in table.Rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < row.Count; c++)
            {
                var cell = new TextBlock
                {
                    Text = row[c],
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(8, 4, 8, 4),
                    IsTextSelectionEnabled = true,
                };
                if (rowIndex == 0)
                {
                    cell.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                }

                grid.Children.Add(cell);
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, c);
            }

            rowIndex++;
        }

        return grid;
    }

    internal static void AddInlines(InlineCollection target, IReadOnlyList<MdInline> inlines)
    {
        foreach (var inline in inlines)
        {
            var mapped = ToInline(inline);
            if (mapped is not null)
            {
                target.Add(mapped);
            }
        }
    }

    private static Inline? ToInline(MdInline inline) => inline switch
    {
        MdText t => new Run { Text = t.Text },
        MdLineBreak => new LineBreak(),
        MdCodeSpan c => new Run { Text = c.Code, FontFamily = new FontFamily("Consolas") },
        MdEmphasis e => Emphasis<Italic>(e.Children),
        MdStrong s => Emphasis<Bold>(s.Children),
        MdStrikethrough s => Emphasis<Span>(s.Children), // No strikethrough in WinUI3 text; render plain.
        MdLink l => Link(l),
        _ => null,
    };

    private static T Emphasis<T>(IReadOnlyList<MdInline> children) where T : Span, new()
    {
        var span = new T();
        AddInlines(span.Inlines, children);
        return span;
    }

    private static Inline Link(MdLink link)
    {
        var hyperlink = new Hyperlink();
        AddInlines(hyperlink.Inlines, link.Children);
        if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            hyperlink.NavigateUri = uri;
            hyperlink.Click += (_, _) => _ = Launcher.LaunchUriAsync(uri);
        }

        return hyperlink;
    }
}
