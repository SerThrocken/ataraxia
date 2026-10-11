using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class ConnectPage : Page
{
    public SettingsViewModel ViewModel { get; } = App.Services.GetRequiredService<SettingsViewModel>();

    public ConnectPage()
    {
        InitializeComponent();
        ViewModel.ContinueRequested += OnContinueRequested;
        Unloaded += (_, _) => ViewModel.ContinueRequested -= OnContinueRequested;
    }

    private void OnContinueRequested(object? sender, EventArgs e) => Frame.Navigate(typeof(ChatPage));

    private void ImportButton_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ImportPage), "connect");
}
