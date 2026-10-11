using Hermes.Client.Core;
using Hermes.Client.WinUI3.ViewModels;
using Hermes.Client.WinUI3.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace Hermes.Client.WinUI3;

public sealed partial class MainWindow : Window
{
    private static MainWindow? _current;
    private readonly IConnectionState _connectionState = App.Services.GetRequiredService<IConnectionState>();

    /// <summary>Opens the chat page for a session (from the sessions list).</summary>
    public static void NavigateToChat(string sessionId, string? title) =>
        _current?.ContentFrame.Navigate(typeof(ChatPage), (sessionId, title));

    public MainWindow()
    {
        InitializeComponent();
        _current = this;

        SystemBackdrop = new MicaBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        GetAppWindow()?.Resize(new Windows.Graphics.SizeInt32(1200, 800));

        _connectionState.StateChanged += (_, state) =>
            DispatcherQueue.TryEnqueue(() => UpdateConnectionIndicator(state));
        UpdateConnectionIndicator(_connectionState.State);

        NavView.Loaded += async (_, _) =>
        {
            var settings = App.Services.GetRequiredService<SettingsViewModel>();
            await settings.InitializeAsync();

            if (!settings.HasStoredKey)
            {
                // First run: no API key yet — show the connect screen.
                ContentFrame.Navigate(typeof(ConnectPage));
                return;
            }

            NavView.SelectedItem = NavView.MenuItems[0];
            ContentFrame.Navigate(typeof(ChatPage));

            // A key is stored: refresh the app-wide state silently.
            await settings.TestConnectionCommand.ExecuteAsync(null);
        };
    }

    private void UpdateConnectionIndicator(ConnectionState state)
    {
        (string text, Windows.UI.Color color) = state switch
        {
            ConnectionState.Connected => ("Connected", Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x3D, 0xBA, 0x6B)),
            ConnectionState.Disconnected => ("Disconnected", Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE5, 0x48, 0x4D)),
            _ => ("Not tested", Microsoft.UI.Colors.Gray),
        };
        ConnectionText.Text = text;
        ConnectionDot.Fill = new SolidColorBrush(color);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        var page = tag switch
        {
            "chat" => typeof(ChatPage),
            "sessions" => typeof(SessionsPage),
            "models" => typeof(ModelsPage),
            "skills" => typeof(SkillsPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(ChatPage),
        };

        ContentFrame.Navigate(page);
    }

    private AppWindow? GetAppWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        return AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
    }
}
