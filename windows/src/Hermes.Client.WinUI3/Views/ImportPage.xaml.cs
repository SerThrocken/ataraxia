using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class ImportPage : Page
{
    public ImportViewModel ViewModel { get; } = App.Services.GetRequiredService<ImportViewModel>();

    public ImportPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync(e.Parameter as string ?? "settings");
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(ViewModel.ReturnPage == "connect" ? typeof(ConnectPage) : typeof(SettingsPage));
}
