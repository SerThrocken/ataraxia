using Hermes.Client.Core;
using Hermes.Client.WinUI3.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace Hermes.Client.WinUI3;

/// <summary>
/// The companion overlay window. Ataraxia's whole ethos is "software that
/// never disturbs you", so this window is built to be invisible to the focus
/// system: WS_EX_NOACTIVATE is forced on before it ever shows (it can never
/// steal focus), it shows with SW_SHOWNA, and it is a tool window (no
/// taskbar button, no Alt+Tab entry). Opacity goes through WS_EX_LAYERED +
/// SetLayeredWindowAttributes; the click-through toggle adds
/// WS_EX_TRANSPARENT. The flag math is unit-tested in
/// <see cref="HudNativeStyle"/>; this file is the one-line application.
/// </summary>
public sealed partial class HudWindow : Window
{
    private readonly HWND _hwnd;
    private readonly HudViewModel _viewModel;
    private bool _dragging;
    private Windows.Foundation.Point _dragOffset;

    public HudWindow(HudViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        Title = "Ataraxia companion";

        HudContent.ViewModel = viewModel;

        _hwnd = new HWND(WindowNative.GetWindowHandle(this));

        // Borderless tool window: no system title bar (the view draws its own
        // drag handle), not resizable/maximizable/minimizable.
        if (AppWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // The never-disturb contract, applied before the first show.
        var exStyle = (uint)PInvoke.GetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle = HudNativeStyle.WithNoActivate(exStyle);
        exStyle = HudNativeStyle.WithLayered(exStyle);
        exStyle = HudNativeStyle.WithToolWindow(exStyle);
        PInvoke.SetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)exStyle);
        PInvoke.SetWindowPos(
            _hwnd,
            HWND.Null,
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);

        ApplyOpacity(_viewModel.Opacity);
        ApplyClickThrough(_viewModel.ClickThrough);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        AppWindow?.Resize(new Windows.Graphics.SizeInt32(300, 164));

        // Drag-to-move on the view's header (no system title bar).
        var handle = HudContent.DragHandle;
        handle.PointerPressed += Handle_PointerPressed;
        handle.PointerMoved += Handle_PointerMoved;
        handle.PointerReleased += Handle_PointerReleased;

        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    /// <summary>Shows without activating — the focused app keeps focus.</summary>
    public void ShowWithoutActivation() =>
        PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNA);

    public void MoveTo(double left, double top) =>
        AppWindow?.Move(new Windows.Graphics.PointInt32(
            (int)Math.Round(left), (int)Math.Round(top)));

    /// <summary>Default spot: bottom-right of the primary work area.</summary>
    public void MoveToDefault()
    {
        var appWindow = AppWindow;
        if (appWindow is null)
        {
            return;
        }

        var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = appWindow.Size;
        MoveTo(work.X + work.Width - size.Width - 16, work.Y + work.Height - size.Height - 16);
    }

    public (int X, int Y) GetPosition()
    {
        var position = AppWindow?.Position ?? new Windows.Graphics.PointInt32(0, 0);
        return (position.X, position.Y);
    }

    private void ApplyOpacity(double opacity) =>
        PInvoke.SetLayeredWindowAttributes(
            _hwnd,
            default,
            HudNativeStyle.OpacityToAlpha(opacity),
            LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

    private void ApplyClickThrough(bool clickThrough)
    {
        var exStyle = (uint)PInvoke.GetWindowLongPtr(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(
            _hwnd,
            WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            (nint)HudNativeStyle.WithClickThrough(exStyle, clickThrough));
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HudViewModel.Opacity))
        {
            ApplyOpacity(_viewModel.Opacity);
        }
        else if (e.PropertyName == nameof(HudViewModel.ClickThrough))
        {
            ApplyClickThrough(_viewModel.ClickThrough);
        }
    }

    private void Handle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var handle = (UIElement)sender;
        _dragging = true;
        _dragOffset = e.GetCurrentPoint(handle).Position;
        handle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Handle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || AppWindow is null)
        {
            return;
        }

        var handle = (UIElement)sender;
        var current = e.GetCurrentPoint(handle).Position;
        var position = AppWindow.Position;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            position.X + (int)Math.Round(current.X - _dragOffset.X),
            position.Y + (int)Math.Round(current.Y - _dragOffset.Y)));
    }

    private void Handle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }
}
