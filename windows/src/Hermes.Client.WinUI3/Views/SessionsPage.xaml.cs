using Hermes.Client.Core;
using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsViewModel ViewModel { get; } = App.Services.GetRequiredService<SessionsViewModel>();

    public SessionsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Sessions.Count == 0)
        {
            await ViewModel.RefreshAsync();
        }
    }

    private void Sessions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not Session session)
        {
            return;
        }

        list.SelectedItem = null; // allow re-selecting the same session
        MainWindow.NavigateToChat(session.Id, session.Title);
    }
}
