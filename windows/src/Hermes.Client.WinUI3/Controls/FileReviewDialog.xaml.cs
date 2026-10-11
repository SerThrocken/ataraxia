using System.Runtime.InteropServices;
using Hermes.Client.Core;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Hermes.Client.WinUI3.Controls;

/// <summary>
/// Modal file-review surface: hosts <see cref="DiffViewer"/> in review mode
/// for one tool call's diffs. The real action is exporting the accepted hunks
/// as a unified-diff patch — the gateway exposes no file-apply route, so the
/// dialog never claims to apply anything.
/// </summary>
public sealed partial class FileReviewDialog : ContentDialog
{
    public FileReviewDialog()
    {
        InitializeComponent();
        Viewer.ExportRequested += OnExportRequested;
    }

    /// <summary>Points the dialog at one tool call's diffs. Call before ShowAsync.</summary>
    public void ShowFor(string toolCallId, IDiffSource diffSource)
    {
        Viewer.DiffSource = diffSource;
        Viewer.ToolCallId = toolCallId;
    }

    // The export button is clicked while this dialog is modal, so our window
    // is the thread's active window. (MainWindow.WindowHandle exists in the
    // repo but this keeps the dialog independent of it.)
    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    private async void OnExportRequested(object? sender, string patch)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, GetActiveWindow());
        picker.SuggestedFileName = "accepted-hunks";
        picker.FileTypeChoices.Add("Unified diff patch", [".patch"]);

        StorageFile? file;
        try
        {
            file = await picker.PickSaveFileAsync();
        }
        catch (Exception ex)
        {
            ShowResult(false, $"Couldn't open the save dialog: {ex.Message}");
            return;
        }

        if (file is null)
        {
            return; // user cancelled
        }

        try
        {
            await FileIO.WriteTextAsync(file, patch);
            ShowResult(true, $"Saved {file.Name} — apply it with patch or git apply.");
        }
        catch (Exception ex)
        {
            ShowResult(false, $"Couldn't save the patch: {ex.Message}");
        }
    }

    private void ShowResult(bool success, string message)
    {
        ResultBar.Severity = success
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Error;
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }
}
