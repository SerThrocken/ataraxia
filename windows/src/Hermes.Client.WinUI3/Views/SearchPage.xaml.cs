using Hermes.Client.Core;
using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Hermes.Client.WinUI3.Views;

/// <summary>
/// Full keyboard navigation: Enter in the query box searches; Down moves
/// focus into the results; Enter on a result opens it in chat; Escape clears
/// the query box.
/// </summary>
public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; } = App.Services.GetRequiredService<SearchViewModel>();

    public SearchPage()
    {
        InitializeComponent();
        ViewModel.OpenHitRequested += (_, hit) => MainWindow.NavigateToChat(hit.SessionId, hit.SessionTitle);
    }

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                _ = ViewModel.SearchAsync();
                break;
            case VirtualKey.Down:
                e.Handled = true;
                if (ResultsList.Items.Count > 0)
                {
                    ResultsList.SelectedIndex = 0;
                    ResultsList.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                }
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                ViewModel.Query = string.Empty;
                break;
        }
    }

    private void ResultsList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ResultsList.SelectedItem is SearchHit hit)
        {
            e.Handled = true;
            ViewModel.OpenHit(hit);
        }
    }
}
