using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Cron-job management: GET /api/jobs plus pause/resume/run/delete per job
/// and creation via the new-job dialog. The gateway answers 501 when its
/// cron module is unavailable; that surfaces as a friendly message, not a
/// stack trace.
/// </summary>
public sealed partial class CronViewModel : ObservableObject
{
    private readonly IGatewayClient _client;

    public CronViewModel(IGatewayClient client)
    {
        _client = client;
    }

    public ObservableCollection<CronJob> Jobs { get; } = new();

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeLabel))]
    [NotifyPropertyChangedFor(nameof(IsJobSelected))]
    public partial CronJob? SelectedJob { get; set; }

    public bool HasError => Error is not null;

    public bool IsJobSelected => SelectedJob is not null;

    public string PauseResumeLabel => SelectedJob?.Enabled == true ? "Pause" : "Resume";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        Error = null;
        try
        {
            var list = await _client.GetCronJobsAsync(includeDisabled: true);
            Jobs.Clear();
            foreach (var job in list.Jobs)
            {
                Jobs.Add(job);
            }
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task TogglePauseAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        Func<CronJob, Task<CronJob>> mutate = SelectedJob.Enabled
            ? job => _client.PauseCronJobAsync(job.Id)
            : job => _client.ResumeCronJobAsync(job.Id);
        await MutateSelectedAsync(mutate, SelectedJob.Enabled ? "pause" : "resume");
    }

    [RelayCommand]
    public async Task RunNowAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        await MutateSelectedAsync(job => _client.RunCronJobAsync(job.Id), "run");
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        var job = SelectedJob;
        if (job is null || IsLoading)
        {
            return;
        }

        IsLoading = true;
        Error = null;
        try
        {
            if (await _client.DeleteCronJobAsync(job.Id))
            {
                Jobs.Remove(job);
                SelectedJob = null;
            }
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<CronJob?> CreateJobAsync(string name, string schedule, string? prompt)
    {
        IsLoading = true;
        Error = null;
        try
        {
            var job = await _client.CreateCronJobAsync(new CronJobCreateRequest(name, schedule, prompt));
            Jobs.Insert(0, job);
            return job;
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task MutateSelectedAsync(Func<CronJob, Task<CronJob>> mutate, string action)
    {
        var job = SelectedJob;
        if (job is null || IsLoading)
        {
            return;
        }

        IsLoading = true;
        Error = null;
        try
        {
            var updated = await mutate(job);
            var index = Jobs.IndexOf(job);
            if (index >= 0)
            {
                Jobs[index] = updated;
                SelectedJob = updated;
            }
        }
        catch (Exception ex)
        {
            Error = $"Could not {action} job: {FriendlyError(ex)}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        GatewayApiException { StatusCode: 401 } =>
            "Invalid gateway API key. Check the connection settings.",
        GatewayApiException { StatusCode: 404 } =>
            "Job not found — it may have been deleted.",
        GatewayApiException { StatusCode: 501 } =>
            "Cron is not available on this gateway.",
        GatewayApiException api => $"Gateway error {api.StatusCode}: {api.Message}",
        HttpRequestException => "Cannot reach the gateway. Check the connection settings.",
        _ => ex.Message,
    };
}
