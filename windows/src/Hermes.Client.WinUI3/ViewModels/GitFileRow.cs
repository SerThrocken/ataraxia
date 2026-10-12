using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.Client.Core.Git;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>One changed file row in the Git page's file list.</summary>
public sealed partial class GitFileRow : ObservableObject
{
    public GitFileRow(GitFileChange change)
    {
        Change = change;
    }

    public GitFileChange Change { get; }

    public string DisplayPath => Change.DisplayPath;

    public string Summary => Change.Summary;

    public string ScreenReaderName => $"{Change.DisplayPath}, {Change.Summary}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
