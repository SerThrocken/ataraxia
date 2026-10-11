using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class MemoryPage : Page
{
    public MemoryViewModel ViewModel { get; } =
        App.Services.GetRequiredService<MemoryViewModel>();

    public MemoryPage()
    {
        InitializeComponent();
    }
}
