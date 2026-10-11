using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Controls;

/// <summary>
/// One file's hunks with review state, backing the review-mode template.
/// </summary>
internal sealed partial class HunkReviewFile
{
    private readonly FileReview _review;

    public HunkReviewFile(FileReview review)
    {
        _review = review;
        Hunks = Enumerable.Range(0, review.HunkCount)
            .Select(i => new HunkRow(review, i))
            .ToList();
    }

    public string DisplayPath => _review.File.NewPath;

    public IReadOnlyList<HunkRow> Hunks { get; }

    public string AcceptAllName => $"Accept all hunks in {DisplayPath}";

    public string RejectAllName => $"Reject all hunks in {DisplayPath}";

    [RelayCommand]
    public void AcceptAll() => _review.AcceptAll();

    [RelayCommand]
    public void RejectAll() => _review.RejectAll();
}

/// <summary>
/// One hunk with its review state, backing the review-mode template.
/// </summary>
internal sealed partial class HunkRow : ObservableObject
{
    private readonly FileReview _review;

    public HunkRow(FileReview review, int index)
    {
        _review = review;
        Index = index;
        _state = review.GetState(index);
        review.StateChanged += OnReviewStateChanged;
    }

    public DiffHunk Hunk => _review.File.Hunks[Index];

    public int Index { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateName))]
    private HunkReview _state;

    public string StateText => _state switch
    {
        HunkReview.Accepted => "Accepted",
        HunkReview.Rejected => "Rejected",
        _ => "Pending",
    };

    public string StateName => $"Hunk {Hunk.Header} in {_review.File.NewPath}: {StateText}";

    public string AcceptName => $"Accept hunk {Hunk.Header} in {_review.File.NewPath}";

    public string RejectName => $"Reject hunk {Hunk.Header} in {_review.File.NewPath}";

    [RelayCommand]
    public void Accept() => _review.Accept(Index);

    [RelayCommand]
    public void Reject() => _review.Reject(Index);

    private void OnReviewStateChanged(object? sender, int index)
    {
        if (index == Index)
        {
            State = _review.GetState(index);
        }
    }
}

/// <summary>
/// Renders agent file-change diffs. Read-only by default; <see cref="IsReviewMode"/>
/// adds per-hunk accept/reject and patch export. The control is a pure view over
/// <see cref="IDiffSource"/>: the chat surface feeds parsed
/// <c>tool.complete</c> <c>inline_diff</c> payloads into a
/// <see cref="ToolDiffRegistry"/> and sets <see cref="ToolCallId"/>.
/// Empty source → honest "no diff" empty state, never placeholder data.
///
/// Review honesty: the gateway (0.21.6) exposes no file-apply route, so the
/// footer says so and the only action is exporting the accepted hunks as a
/// unified-diff patch.
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

    public static readonly DependencyProperty IsReviewModeProperty =
        DependencyProperty.Register(
            nameof(IsReviewMode), typeof(bool), typeof(DiffViewer),
            new PropertyMetadata(false, OnSourceChanged));

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

    public bool IsReviewMode
    {
        get => (bool)GetValue(IsReviewModeProperty);
        set => SetValue(IsReviewModeProperty, value);
    }

    /// <summary>
    /// Raised with the exported unified-diff patch text when the review
    /// footer export button is pressed. The host performs the actual save.
    /// </summary>
    public event EventHandler<string>? ExportRequested;

    private List<FileReview> _reviews = [];

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

        _reviews = files.Select(f => new FileReview(f)).ToList();

        EmptyText.Visibility = Visibility.Collapsed;
        var review = IsReviewMode;
        FilesList.Visibility = review ? Visibility.Collapsed : Visibility.Visible;
        ReviewPanel.Visibility = review ? Visibility.Visible : Visibility.Collapsed;

        if (review)
        {
            foreach (var r in _reviews)
            {
                r.StateChanged += OnReviewStateChanged;
            }

            ReviewFilesList.ItemsSource = _reviews.Select(r => new HunkReviewFile(r)).ToList();
            UpdateReviewFooter();
        }
        else
        {
            FilesList.ItemsSource = files;
        }
    }

    private void OnReviewStateChanged(object? sender, int hunkIndex) => UpdateReviewFooter();

    private void UpdateReviewFooter()
    {
        var accepted = _reviews.Sum(r => r.AcceptedCount);
        var rejected = _reviews.Sum(r => r.RejectedCount);
        var pending = _reviews.Sum(r => r.PendingCount);
        ReviewCounts.Text = $"{accepted} accepted · {rejected} rejected · {pending} pending";
        ExportButton.IsEnabled = accepted > 0;
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var patch = PatchExporter.BuildPatch(_reviews);
        if (patch.Length > 0)
        {
            ExportRequested?.Invoke(this, patch);
        }
    }

    private void ShowEmpty()
    {
        _reviews = [];
        FilesList.ItemsSource = null;
        ReviewFilesList.ItemsSource = null;
        FilesList.Visibility = Visibility.Collapsed;
        ReviewPanel.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Visible;
    }
}
