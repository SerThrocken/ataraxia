using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class GitPage : Page
{
    public GitViewModel ViewModel { get; } = App.Services.GetRequiredService<GitViewModel>();

    public GitPage()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await ViewModel.OpenRepositoryAsync(folder.Path);
        }
    }

    private void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is GitFileRow row)
        {
            _ = ViewModel.ShowDiffAsync(row);
        }
    }
}
