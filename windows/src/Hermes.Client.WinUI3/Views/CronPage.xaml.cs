using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Views;

public sealed partial class CronPage : Page
{
    public CronViewModel ViewModel { get; } = App.Services.GetRequiredService<CronViewModel>();

    public CronPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private async void NewJob_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            Header = "Name",
            PlaceholderText = "morning brief",
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetName(nameBox, "Job name");
        var scheduleBox = new TextBox
        {
            Header = "Schedule",
            PlaceholderText = "0 7 * * *  or  every 30m  or  2026-10-12T07:00",
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetName(scheduleBox, "Job schedule");
        var promptBox = new TextBox
        {
            Header = "Prompt",
            PlaceholderText = "What the agent should do when the job fires",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
        };
        AutomationProperties.SetName(promptBox, "Job prompt");

        var dialog = new ContentDialog
        {
            Title = "New cron job",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel
            {
                Children = { nameBox, scheduleBox, promptBox },
            },
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.CreateJobAsync(
                nameBox.Text.Trim(),
                scheduleBox.Text.Trim(),
                string.IsNullOrWhiteSpace(promptBox.Text) ? null : promptBox.Text.Trim());
        }
    }

    private async void RunNow_Click(object sender, RoutedEventArgs e)
    {
        var job = ViewModel.SelectedJob;
        if (job is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Run now?",
            Content = $"Trigger \"{job.Name}\" immediately? The job will execute its prompt now.",
            PrimaryButtonText = "Run",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RunNowCommand.ExecuteAsync(null);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var job = ViewModel.SelectedJob;
        if (job is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete job?",
            Content = $"Delete \"{job.Name}\"? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteCommand.ExecuteAsync(null);
        }
    }
}
