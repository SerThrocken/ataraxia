using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Hermes.Client.WinUI3.Services;
using Microsoft.UI.Dispatching;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// A run tracked by Mission Control: live status plus its event timeline.
/// </summary>
public sealed partial class TrackedRun : ObservableObject
{
    public TrackedRun(string runId) => RunId = runId;

    public string RunId { get; }

    public ObservableCollection<RunEvent> Events { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    public partial RunStatus? Status { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string DisplayStatus => Status?.Status ?? "starting…";

    public bool IsActive => Status is null || !Status.IsTerminal;

    public string ShortId => RunId.Length > 16 ? RunId[..16] + "…" : RunId;

    /// <summary>The request id of the latest pending approval.request event, if any.</summary>
    public string? LatestApprovalRequestId
    {
        get
        {
            for (var i = Events.Count - 1; i >= 0; i--)
            {
                if (Events[i].IsApprovalRequest)
                {
                    return Events[i].RequestId;
                }
            }

            return null;
        }
    }
}

/// <summary>
/// Mission control for agent runs. The gateway exposes no run list
/// (GET /v1/runs is 405), so this tracks runs the app knows about —
/// started here, or opened from a toast deep link — and polls each with
/// exponential backoff until terminal. Event timelines stream over SSE;
/// completed runs replay through the scrubber.
/// </summary>
public sealed partial class MissionControlViewModel : ObservableObject, IDisposable
{
    private readonly IGatewayClient _client;
    private readonly RunToastService _toasts;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<string, CancellationTokenSource> _trackers = new();
    private DispatcherQueueTimer? _replayTimer;
    private TrackedRun? _selectedRunHooked;
    private bool _disposed;

    public MissionControlViewModel(IGatewayClient client, RunToastService toasts)
    {
        _client = client;
        _toasts = toasts;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public ObservableCollection<TrackedRun> Runs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplayEvent))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowApprovalActions))]
    [NotifyPropertyChangedFor(nameof(CanControlRun))]
    public partial TrackedRun? SelectedRun { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    [ObservableProperty]
    public partial bool FleetUnknown { get; set; } = true;

    [ObservableProperty]
    public partial int FleetActiveRuns { get; set; }

    [ObservableProperty]
    public partial int FleetStoredRuns { get; set; }

    // ---- Replay scrubber ----

    [ObservableProperty]
    public partial bool IsReplaying { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplayEvent))]
    public partial double ReplayIndex { get; set; }

    [ObservableProperty]
    public partial int ReplayMaximum { get; set; }

    [ObservableProperty]
    public partial bool ReplayPlaying { get; set; }

    public RunEvent? ReplayEvent =>
        SelectedRun is { } run && (int)ReplayIndex >= 0 && (int)ReplayIndex < run.Events.Count
            ? run.Events[(int)ReplayIndex]
            : null;

    public string FleetSummary => FleetUnknown
        ? "Fleet status unknown"
        : $"{FleetActiveRuns} active • {FleetStoredRuns} stored runs";

    public bool HasSelection => SelectedRun is not null;

    public bool ShowApprovalActions => SelectedRun?.Status?.IsWaitingForApproval == true;

    public bool CanControlRun => SelectedRun?.IsActive == true;

    partial void OnSelectedRunChanged(TrackedRun? value)
    {
        if (_selectedRunHooked is not null)
        {
            _selectedRunHooked.PropertyChanged -= TrackedRun_PropertyChanged;
            _selectedRunHooked = null;
        }

        if (value is not null)
        {
            value.PropertyChanged += TrackedRun_PropertyChanged;
            _selectedRunHooked = value;
            ReplayMaximum = Math.Max(0, value.Events.Count - 1);
        }

        EndReplay();
    }

    private void TrackedRun_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrackedRun.Status) or nameof(TrackedRun.IsActive))
        {
            OnPropertyChanged(nameof(ShowApprovalActions));
            OnPropertyChanged(nameof(CanControlRun));
        }
    }

    /// <summary>Tracks a run (idempotent) and selects it.</summary>
    public void TrackRun(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var run = Runs.FirstOrDefault(r => r.RunId == runId);
        if (run is null)
        {
            run = new TrackedRun(runId);
            Runs.Add(run);
            StartTracking(run);
        }

        SelectedRun = run;
    }

    /// <summary>Starts a new agent run and tracks it. Returns the run id.</summary>
    public async Task<string> StartRunAsync(string input, string? sessionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        IsLoading = true;
        Error = null;
        try
        {
            var created = await _client.CreateRunAsync(input, sessionId);
            TrackRun(created.RunId);
            return created.RunId;
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SteerRunAsync(TrackedRun run, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        try
        {
            await _client.SteerRunAsync(run.RunId, text);
        }
        catch (Exception ex)
        {
            run.Error = FriendlyError(ex);
            throw;
        }
    }

    public async Task StopRunAsync(TrackedRun run)
    {
        try
        {
            var status = await _client.StopRunAsync(run.RunId);
            _dispatcher.TryEnqueue(() => run.Status = status);
        }
        catch (Exception ex)
        {
            run.Error = FriendlyError(ex);
            throw;
        }
    }

    public Task ApproveAsync(TrackedRun run) =>
        ResolveAsync(run, "once");

    public Task DenyAsync(TrackedRun run) =>
        ResolveAsync(run, "deny");

    private async Task ResolveAsync(TrackedRun run, string choice)
    {
        try
        {
            await _client.ResolveApprovalAsync(run.RunId, choice, run.LatestApprovalRequestId);
        }
        catch (Exception ex)
        {
            run.Error = FriendlyError(ex);
            throw;
        }
    }

    [RelayCommand]
    public async Task RefreshFleetAsync()
    {
        try
        {
            var health = await _client.GetDetailedHealthAsync();
            if (health.Platforms.TryGetValue("api_server", out var api) && api.Metrics is { } m)
            {
                FleetActiveRuns = m.ActiveRuns;
                FleetStoredRuns = m.StoredRuns;
                FleetUnknown = false;
                OnPropertyChanged(nameof(FleetSummary));
            }
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
    }

    // ---- Replay ----

    [RelayCommand]
    public void BeginReplay()
    {
        if (SelectedRun is null || SelectedRun.Events.Count == 0)
        {
            return;
        }

        IsReplaying = true;
        ReplayIndex = 0;
        ResumeReplay();
    }

    [RelayCommand]
    public void PauseReplay()
    {
        _replayTimer?.Stop();
        ReplayPlaying = false;
    }

    [RelayCommand]
    public void ResumeReplay()
    {
        if (!IsReplaying)
        {
            return;
        }

        StopReplayTimer();
        _replayTimer = _dispatcher.CreateTimer();
        _replayTimer.Interval = TimeSpan.FromMilliseconds(700);
        _replayTimer.Tick += (_, _) =>
        {
            if (SelectedRun is null || ReplayIndex + 1 >= SelectedRun.Events.Count)
            {
                PauseReplay();
                return;
            }

            ReplayIndex++;
        };
        _replayTimer.Start();
        ReplayPlaying = true;
    }

    [RelayCommand]
    public void EndReplay()
    {
        StopReplayTimer();
        IsReplaying = false;
        ReplayPlaying = false;
    }

    private void StopReplayTimer()
    {
        if (_replayTimer is not null)
        {
            _replayTimer.Stop();
            _replayTimer = null;
        }
    }

    // ---- Tracking ----

    private void StartTracking(TrackedRun run)
    {
        var cts = new CancellationTokenSource();
        _trackers[run.RunId] = cts;
        _ = PollLoopAsync(run, cts.Token);
        _ = EventLoopAsync(run, cts.Token);
    }

    private async Task PollLoopAsync(TrackedRun run, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        string? lastStatus = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                RunStatus status;
                try
                {
                    status = await _client.GetRunAsync(run.RunId, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    var message = FriendlyError(ex);
                    _dispatcher.TryEnqueue(() => run.Error = message);
                    delay = NextDelay(delay);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    continue;
                }

                var changed = status.Status != lastStatus;
                lastStatus = status.Status;
                _dispatcher.TryEnqueue(() =>
                {
                    run.Status = status;
                    run.Error = null;
                });
                if (status.IsTerminal)
                {
                    var terminal = status;
                    _dispatcher.TryEnqueue(() => _toasts.ShowCompleted(terminal));
                    break;
                }

                delay = changed ? TimeSpan.FromSeconds(2) : NextDelay(delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Untracked or disposed.
        }
    }

    private async Task EventLoopAsync(TrackedRun run, CancellationToken ct)
    {
        try
        {
            await foreach (var e in _client.StreamRunEventsAsync(run.RunId, null, ct).ConfigureAwait(false))
            {
                var captured = e;
                _dispatcher.TryEnqueue(() =>
                {
                    run.Events.Add(captured);
                    ReplayMaximum = Math.Max(0, run.Events.Count - 1);
                    OnPropertyChanged(nameof(ReplayEvent));
                    if (captured.IsApprovalRequest)
                    {
                        _toasts.ShowApproval(run.RunId, captured.ToApprovalRequest());
                    }
                });
                if (e.IsTerminal)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Untracked or disposed.
        }
        catch (Exception ex)
        {
            // SSE dropped; status polling continues to carry the run.
            var message = FriendlyError(ex);
            _dispatcher.TryEnqueue(() => run.Error ??= message);
        }
    }

    private static TimeSpan NextDelay(TimeSpan current) =>
        TimeSpan.FromSeconds(Math.Min(current.TotalSeconds * 2, 30));

    private static string FriendlyError(Exception ex) => ex switch
    {
        GatewayApiException { StatusCode: 401 } =>
            "Invalid gateway API key. Check the connection settings.",
        GatewayApiException { StatusCode: 404 } =>
            "Run not found. It may have expired on the gateway.",
        GatewayApiException { StatusCode: 409 } api =>
            $"Gateway refused: {api.Message}",
        GatewayApiException api => $"Gateway error {api.StatusCode}: {api.Message}",
        HttpRequestException => "Cannot reach the gateway. Check the connection settings.",
        _ => ex.Message,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopReplayTimer();
        foreach (var cts in _trackers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _trackers.Clear();
    }
}
