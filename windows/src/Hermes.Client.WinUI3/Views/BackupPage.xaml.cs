using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class BackupPage : Page
{
    public BackupViewModel ViewModel { get; } =
        App.Services.GetRequiredService<BackupViewModel>();

    public BackupPage()
    {
        InitializeComponent();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = "ataraxia-client-backup",
        };
        picker.FileTypeChoices.Add("JSON backup", new List<string> { ".json" });
        InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return; // User cancelled.
        }

        await ViewModel.ExportAsync(file.Path);
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return; // User cancelled.
        }

        await ViewModel.ImportAsync(file.Path);
    }
}
