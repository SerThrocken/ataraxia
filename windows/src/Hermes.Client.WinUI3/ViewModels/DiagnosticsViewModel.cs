using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// One-tap diagnostics: runs the native checks in
/// <see cref="DiagnosticsRunner"/> (the ops/doctor route is
/// serve-backend-only on the Acer and not exposed on 8642) and renders
/// each result natively with pass / warn / fail state.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly DiagnosticsRunner _runner;

    public DiagnosticsViewModel(IGatewayClient client)
    {
        _runner = new DiagnosticsRunner(client);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public bool HasError => Error is not null;

    public ObservableCollection<DiagnosticCheck> Checks { get; } = new();

    public string Summary => Checks.Count == 0
        ? "Not run yet."
        : $"{Checks.Count(c => c.Status == DiagnosticStatus.Pass)} passed · " +
          $"{Checks.Count(c => c.Status == DiagnosticStatus.Warn)} warnings · " +
          $"{Checks.Count(c => c.Status == DiagnosticStatus.Fail)} failed";

    public string ScreenReaderSummary => Checks.Count == 0
        ? "Diagnostics have not been run yet."
        : "Diagnostics complete. " + Summary + ". " +
          string.Join(". ", Checks.Select(c => $"{c.Name}: {c.Status}, {c.Detail}"));

    [RelayCommand]
    public async Task RunAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        Error = null;
        try
        {
            var report = await _runner.RunAsync();
            Checks.Clear();
            foreach (var check in report.Checks)
            {
                Checks.Add(check);
            }

            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(ScreenReaderSummary));
        }
        catch (Exception ex)
        {
            Error = $"Diagnostics failed to run: {Trim(ex.Message)}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
