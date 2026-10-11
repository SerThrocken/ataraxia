using Hermes.Client.WinUI3.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hermes.Client.WinUI3.Views;

/// <summary>
/// Plain-language schedule dialog. Shown from the Cron page ("Worker A owns
/// the CronPage; this dialog is worker E's"); returns the created
/// <see cref="Hermes.Client.Core.CronJob"/> or null.
/// </summary>
public sealed partial class ScheduleDialog : ContentDialog
{
    public ScheduleDialogViewModel ViewModel { get; } =
        App.Services.GetRequiredService<ScheduleDialogViewModel>();

    public ScheduleDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the dialog and runs the create flow. Primary click creates the
    /// job and keeps the dialog open to show the result; closing returns the
    /// created job (null when nothing was created).
    /// </summary>
    public static async Task<Hermes.Client.Core.CronJob?> ShowAsync(FrameworkElement anchor)
    {
        var dialog = new ScheduleDialog { XamlRoot = anchor.XamlRoot };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true; // keep open: run create, show the result inline
            await dialog.ViewModel.CreateAsync();
        };

        await dialog.ShowAsync();
        return dialog.ViewModel.CreatedJob;
    }
}
