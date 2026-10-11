using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class HandoffPage : Page
{
    public HandoffViewModel ViewModel { get; } = App.Services.GetRequiredService<HandoffViewModel>();

    public HandoffPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string sessionId && !string.IsNullOrWhiteSpace(sessionId))
        {
            ViewModel.Initialize(sessionId);
        }
    }
}
