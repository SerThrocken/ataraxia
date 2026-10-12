using Hermes.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Hermes.Client.WinUI3.Controls;

/// <summary>
/// The companion overlay's content: status dot + gateway text, active run
/// line, and the three quick actions. Esc anywhere inside dismisses via the
/// view model's close command. Styling is theme-resource only so
/// high-contrast themes track automatically.
/// </summary>
public sealed partial class HudView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(HudViewModel),
            typeof(HudView),
            new PropertyMetadata(null, static (d, _) => ((HudView)d).Bindings.Update()));

    public HudViewModel? ViewModel
    {
        get => (HudViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>The drag handle, exposed so the hosting window can move itself.</summary>
    public FrameworkElement DragHandle => HeaderGrid;

    public HudView()
    {
        InitializeComponent();
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ViewModel?.CloseHudCommand.Execute(null);
    }
}
