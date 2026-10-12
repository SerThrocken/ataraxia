using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class TerminalPage : Page
{
    public TerminalPageViewModel ViewModel { get; } =
        App.Services.GetRequiredService<TerminalPageViewModel>();

    public TerminalPage()
    {
        InitializeComponent();
        ViewModel.SessionReplaced += (_, _) => TerminalView.Session = ViewModel.Session;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Re-attach the (possibly surviving) session, then size the new one
        // to this view. MeasureTerminal falls back to 80x24 pre-layout; the
        // control's debounced resize corrects it once layout lands.
        TerminalView.Session = ViewModel.Session;
        var (cols, rows) = TerminalView.MeasureTerminal();
        await ViewModel.InitializeAsync(cols, rows);
    }
}
