using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class ModelsPage : Page
{
    public ModelsViewModel ViewModel { get; } = App.Services.GetRequiredService<ModelsViewModel>();

    public ModelsPage()
    {
        InitializeComponent();
    }
}
