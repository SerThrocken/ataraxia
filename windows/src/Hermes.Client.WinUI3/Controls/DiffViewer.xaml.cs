using Hermes.Client.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Controls;

/// <summary>
/// Renders agent file-change diffs. The control is a pure view over
/// <see cref="IDiffSource"/>: the chat surface feeds parsed
/// <c>tool.complete</c> <c>inline_diff</c> payloads into a
/// <see cref="ToolDiffRegistry"/> and sets <see cref="ToolCallId"/>.
/// Empty source → honest "no diff" empty state, never placeholder data.
/// </summary>
public sealed partial class DiffViewer : UserControl
{
    public static readonly DependencyProperty DiffSourceProperty =
        DependencyProperty.Register(
            nameof(DiffSource), typeof(IDiffSource), typeof(DiffViewer),
            new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty ToolCallIdProperty =
        DependencyProperty.Register(
            nameof(ToolCallId), typeof(string), typeof(DiffViewer),
            new PropertyMetadata(null, OnSourceChanged));

    public IDiffSource? DiffSource
    {
        get => (IDiffSource?)GetValue(DiffSourceProperty);
        set => SetValue(DiffSourceProperty, value);
    }

    public string? ToolCallId
    {
        get => (string?)GetValue(ToolCallIdProperty);
        set => SetValue(ToolCallIdProperty, value);
    }

    public DiffViewer()
    {
        InitializeComponent();
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DiffViewer)d).RefreshAsync();

    private async void RefreshAsync()
    {
        var source = DiffSource;
        var toolCallId = ToolCallId;
        if (source is null || string.IsNullOrWhiteSpace(toolCallId))
        {
            ShowEmpty();
            return;
        }

        IReadOnlyList<DiffFile> files;
        try
        {
            files = await source.GetDiffAsync(toolCallId);
        }
        catch
        {
            // A failing source is an empty diff, not a crash.
            ShowEmpty();
            return;
        }

        if (files.Count == 0)
        {
            ShowEmpty();
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        FilesList.Visibility = Visibility.Visible;
        FilesList.ItemsSource = files;
    }

    private void ShowEmpty()
    {
        FilesList.ItemsSource = null;
        FilesList.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Visible;
    }
}
