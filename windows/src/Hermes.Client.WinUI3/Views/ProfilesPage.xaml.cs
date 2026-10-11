using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; } =
        App.Services.GetRequiredService<ProfilesViewModel>();

    public ProfilesPage()
    {
        InitializeComponent();
    }
}
