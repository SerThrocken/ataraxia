using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class ChatPage : Page
{
    public ChatViewModel ViewModel { get; } = App.Services.GetRequiredService<ChatViewModel>();

    public ChatPage()
    {
        InitializeComponent();
        ViewModel.AnnounceRequested += OnAnnounceRequested;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ValueTuple<string, string?> session)
        {
            await ViewModel.OpenSessionAsync(session.Item1, session.Item2);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.CancelStreaming();
        ViewModel.Detach();
        ViewModel.AnnounceRequested -= OnAnnounceRequested;
    }

    /// <summary>Moves keyboard focus to the message composer (command palette).</summary>
    public void FocusComposer() => MessageInput.Focus(FocusState.Programmatic);

    /// <summary>
    /// Relays ViewModel queue-state changes to screen readers as a live-region
    /// notification on the page itself (always in the automation tree).
    /// </summary>
    private void OnAnnounceRequested(object? sender, string message)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(this);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            message,
            "ataraxia-queue-status");
    }

    private void MessageInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (ViewModel.SendCommand.CanExecute(null))
            {
                ViewModel.SendCommand.Execute(null);
            }
        }
    }

    private async void OnReviewDiffClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DiffItem item)
        {
            return;
        }

        var dialog = new Controls.FileReviewDialog { XamlRoot = XamlRoot };
        dialog.ShowFor(item.ToolCallId, item.DiffSource);
        await dialog.ShowAsync();
    }
}
