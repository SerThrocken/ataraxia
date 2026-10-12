using Hermes.Client.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;

namespace Hermes.Client.WinUI3;

/// <summary>
/// Minimal quick-entry box summoned by the global hotkey from anywhere.
/// Submits via <see cref="QuickEntryService"/> (POST /api/sessions, then
/// POST /api/sessions/{id}/chat), navigates the main window to the new
/// session, and closes. Enter sends, Esc cancels.
/// </summary>
public sealed partial class QuickEntryWindow : Window
{
    private readonly QuickEntryService _quickEntry;
    private bool _sending;

    public QuickEntryWindow()
    {
        _quickEntry = new QuickEntryService(App.Services.GetRequiredService<IGatewayClient>());
        InitializeComponent();
        Title = "Ataraxia quick entry";

        var hwnd = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow?.Resize(new Windows.Graphics.SizeInt32(460, 250));

        Activated += (_, _) => InputBox.Focus(FocusState.Programmatic);
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = SendAsync();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async Task SendAsync()
    {
        if (_sending)
        {
            return;
        }

        var text = InputBox.Text.Trim();
        if (text.Length == 0)
        {
            Close();
            return;
        }

        _sending = true;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            var session = await _quickEntry.SubmitAsync(text);
            MainWindow.NavigateToChat(
                session.Id,
                string.IsNullOrWhiteSpace(session.Title) ? "Quick entry" : session.Title);
            Close();
        }
        catch (Exception ex)
        {
            // Stay open with the text intact so nothing the user typed is lost.
            ErrorText.Text = $"Couldn't send: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
            _sending = false;
        }
    }
}
