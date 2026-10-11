using Hermes.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Hermes.Client.WinUI3.Controls;

/// <summary>
/// Ctrl+K overlay: search box + ranked results. Focus stays in the search
/// box; Up/Down moves selection, Enter runs, Esc dismisses. All strings are
/// screen-reader named; styling is theme-resource only (high-contrast safe).
/// </summary>
public sealed partial class CommandPaletteControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(CommandPaletteViewModel),
            typeof(CommandPaletteControl),
            new PropertyMetadata(null, static (d, _) => ((CommandPaletteControl)d).Bindings.Update()));

    public CommandPaletteViewModel? ViewModel
    {
        get => (CommandPaletteViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public CommandPaletteControl()
    {
        InitializeComponent();
    }

    public void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

    private void DismissLayer_Tapped(object sender, TappedRoutedEventArgs e) =>
        ViewModel?.RequestDismiss();

    private async void SearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Down:
                e.Handled = true;
                vm.MoveSelection(1);
                break;
            case VirtualKey.Up:
                e.Handled = true;
                vm.MoveSelection(-1);
                break;
            case VirtualKey.Enter:
                e.Handled = true;
                await vm.ExecuteSelectedAsync();
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                vm.RequestDismiss();
                break;
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Keep the keyboard-driven selection visible while scrolling.
        if (ResultsList.SelectedIndex >= 0 && ResultsList.SelectedIndex < ViewModel?.Results.Count)
        {
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
        }
    }

    private async void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        vm.SelectIndex(vm.Results.IndexOf((Hermes.Client.Core.PaletteItem)e.ClickedItem));
        await vm.ExecuteSelectedAsync();
    }
}
