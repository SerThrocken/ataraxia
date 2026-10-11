using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Hermes.Client.WinUI3.Views;

/// <summary>
/// Mission Control: live agent runs. The left list tracks known runs; the
/// detail pane shows status, the event timeline, and the replay scrubber.
/// Steer/stop/approve/deny actions run through confirmation or input
/// dialogs; destructive stop always confirms first.
/// </summary>
public sealed partial class MissionControlPage : Page
{
    public MissionControlViewModel ViewModel { get; } =
        App.Services.GetRequiredService<MissionControlViewModel>();

    public MissionControlPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Toast deep link (or programmatic navigation) carries a run id.
        if (e.Parameter is string runId && !string.IsNullOrWhiteSpace(runId))
        {
            ViewModel.TrackRun(runId);
        }

        await ViewModel.RefreshFleetAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Stop the per-run poll/event loops; re-navigating re-tracks.
        ViewModel.Dispose();
    }

    private TrackedRun? Selected => ViewModel.SelectedRun;

    private async void NewRunButton_Click(object sender, RoutedEventArgs e)
    {
        var input = await PromptAsync("Start agent run", "Task for the agent", "Describe the task…");
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        try
        {
            await ViewModel.StartRunAsync(input);
        }
        catch (Exception)
        {
            // The view model surfaces the friendly error; the dialog just closes.
        }
    }

    private async void WatchRunButton_Click(object sender, RoutedEventArgs e)
    {
        var runId = await PromptAsync("Watch run", "Run id", "run_…");
        if (!string.IsNullOrWhiteSpace(runId))
        {
            ViewModel.TrackRun(runId.Trim());
        }
    }

    private async void SteerButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } run)
        {
            return;
        }

        var text = await PromptAsync("Steer run", "Guidance for the agent", "Type guidance…");
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            await ViewModel.SteerRunAsync(run, text);
        }
        catch (Exception)
        {
            // Friendly error is set on the run by the view model.
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } run)
        {
            return;
        }

        // Destructive action: interrupt the agent mid-turn — always confirm.
        var dialog = new ContentDialog
        {
            Title = "Stop run?",
            Content = $"Interrupt the agent run {run.ShortId}? In-flight work is discarded.",
            PrimaryButtonText = "Stop run",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await ViewModel.StopRunAsync(run);
        }
        catch (Exception)
        {
            // Friendly error is set on the run by the view model.
        }
    }

    private async void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } run)
        {
            await ResolveQuietly(() => ViewModel.ApproveAsync(run));
        }
    }

    private async void DenyButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } run)
        {
            await ResolveQuietly(() => ViewModel.DenyAsync(run));
        }
    }

    private static async Task ResolveQuietly(Func<Task> resolve)
    {
        try
        {
            await resolve();
        }
        catch (Exception)
        {
            // Friendly error is set on the run by the view model.
        }
    }

    /// <summary>Single-line text input dialog. Null when cancelled.</summary>
    private async Task<string?> PromptAsync(string title, string label, string placeholder)
    {
        var box = new TextBox { PlaceholderText = placeholder };
        AutomationProperties.SetName(box, label);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                dialog.Hide();
            }
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? box.Text
            : null;
    }
}
