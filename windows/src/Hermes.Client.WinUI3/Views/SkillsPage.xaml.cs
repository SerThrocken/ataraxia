using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class SkillsPage : Page
{
    public SkillsViewModel ViewModel { get; } = App.Services.GetRequiredService<SkillsViewModel>();

    public SkillsPage()
    {
        InitializeComponent();
    }
}
