using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Plain-language scheduled prompts: the user types "brief me every morning
/// at 7", the rule-based <see cref="ScheduleParser"/> maps it to a cron
/// expression + prompt, and Create posts it to POST /api/jobs.
/// </summary>
public sealed partial class ScheduleDialogViewModel : ObservableObject
{
    private readonly IGatewayClient _gateway;

    [ObservableProperty]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ParsedSchedule? Parsed { get; set; }

    [ObservableProperty]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCreating { get; set; }

    [ObservableProperty]
    public partial string ResultText { get; set; } = string.Empty;

    /// <summary>The job created by <see cref="CreateAsync"/>; null until created.</summary>
    public CronJob? CreatedJob { get; private set; }

    public bool CanCreate => Parsed is not null && !IsCreating;

    public ScheduleDialogViewModel(IGatewayClient gateway) => _gateway = gateway;

    partial void OnInputTextChanged(string value) => Parse();

    private void Parse()
    {
        CreatedJob = null;
        ResultText = string.Empty;
        if (ScheduleParser.TryParse(InputText, out var parsed))
        {
            Parsed = parsed;
            ErrorText = string.Empty;
        }
        else
        {
            Parsed = null;
            ErrorText = string.IsNullOrWhiteSpace(InputText)
                ? string.Empty
                : "I couldn't parse a schedule from that. Try \"every morning at 7\" or \"every weekday at 9am\".";
        }

        OnPropertyChanged(nameof(CanCreate));
    }

    [RelayCommand]
    public async Task CreateAsync()
    {
        if (Parsed is null || IsCreating)
        {
            return;
        }

        IsCreating = true;
        OnPropertyChanged(nameof(CanCreate));
        try
        {
            var request = ScheduleParser.ToCreateRequest(Parsed);
            CreatedJob = await _gateway.CreateCronJobAsync(request);
            ResultText = CreatedJob.State == "paused"
                ? $"Created \"{CreatedJob.Name}\" — paused, awaiting operator approval in the gateway."
                : $"Created \"{CreatedJob.Name}\" — next run {CreatedJob.NextRunAt?.LocalDateTime:g}.";
        }
        catch (GatewayApiException ex)
        {
            ResultText = $"Couldn't create the job: {ex.Message}";
        }
        finally
        {
            IsCreating = false;
            OnPropertyChanged(nameof(CanCreate));
        }
    }
}
