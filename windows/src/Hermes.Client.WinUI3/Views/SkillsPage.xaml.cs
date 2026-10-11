using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class SkillsPage : Page
{
    public SkillsViewModel ViewModel { get; } = App.Services.GetRequiredService<SkillsViewModel>();

    public SkillsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (ViewModel.Skills.Count == 0 && ViewModel.Toolsets.Count == 0)
        {
            await ViewModel.RefreshAsync();
        }
    }
}
