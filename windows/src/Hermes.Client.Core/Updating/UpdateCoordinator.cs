using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hermes.Client.Core;

/// <summary>
/// UI-agnostic self-update state machine for the client app itself. All
/// user-facing strings live here so the view stays thin; the view binds to
/// these properties and forwards the three actions.
/// </summary>
public sealed class UpdateCoordinator : INotifyPropertyChanged
{
    private const string NoFeedMessage =
        "No update feed is configured yet, so self-updates are unavailable.";
    private const string DevBuildMessage =
        "Self-updates work only from an installed build — this looks like a dev/debug build.";

    private readonly IAppUpdater _updater;
    private UpdateState _state;
    private string _message = string.Empty;
    private AppUpdateInfo? _pendingUpdate;
    private double _progress;

    public event PropertyChangedEventHandler? PropertyChanged;

    public UpdateCoordinator(IAppUpdater updater)
    {
        _updater = updater;
        if (updater.IsFeedConfigured)
        {
            State = UpdateState.Idle;
            Message = "Check for updates to see if a new version is available.";
        }
        else
        {
            State = UpdateState.NotConfigured;
            Message = NoFeedMessage;
        }
    }

    public UpdateState State
    {
        get => _state;
        private set
        {
            if (SetField(ref _state, value))
            {
                Raise(nameof(IsBusy));
                Raise(nameof(CanCheck));
                Raise(nameof(CanDownload));
                Raise(nameof(CanApply));
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    /// <summary>Download progress, 0..1.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public AppUpdateInfo? PendingUpdate
    {
        get => _pendingUpdate;
        private set => SetField(ref _pendingUpdate, value);
    }

    public string CurrentVersion => _updater.CurrentVersion;

    public bool IsBusy => State is UpdateState.Checking or UpdateState.Downloading;

    public bool CanCheck => _updater.IsFeedConfigured && !IsBusy;

    public bool CanDownload => State == UpdateState.UpdateAvailable && !IsBusy;

    public bool CanApply => State == UpdateState.ReadyToRestart;

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!_updater.IsFeedConfigured)
        {
            State = UpdateState.NotConfigured;
            Message = NoFeedMessage;
            return;
        }

        State = UpdateState.Checking;
        Message = "Checking for updates…";
        try
        {
            var update = await _updater.CheckForUpdatesAsync(cancellationToken);
            PendingUpdate = update;
            if (update is null)
            {
                State = UpdateState.UpToDate;
                Message = $"You're on the latest version ({_updater.CurrentVersion}).";
            }
            else
            {
                State = UpdateState.UpdateAvailable;
                Message = $"Version {update.Version} is available.";
            }
        }
        catch (AppNotInstalledException)
        {
            State = UpdateState.NotInstalled;
            Message = DevBuildMessage;
        }
        catch (Exception ex)
        {
            State = UpdateState.Error;
            Message = $"Couldn't check for updates: {Trim(ex.Message)}";
        }
    }

    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        var update = PendingUpdate;
        if (update is null || State != UpdateState.UpdateAvailable)
        {
            return;
        }

        State = UpdateState.Downloading;
        Message = $"Downloading version {update.Version}…";
        Progress = 0;
        try
        {
            var progress = new Progress<double>(p => Progress = p);
            await _updater.DownloadAsync(update, progress, cancellationToken);
            State = UpdateState.ReadyToRestart;
            Message = $"Version {update.Version} is downloaded. Restart to install it.";
        }
        catch (AppNotInstalledException)
        {
            State = UpdateState.NotInstalled;
            Message = DevBuildMessage;
        }
        catch (Exception ex)
        {
            State = UpdateState.Error;
            Message = $"Couldn't download the update: {Trim(ex.Message)}";
        }
    }

    /// <summary>
    /// Applies the downloaded update and restarts the app. Only valid from
    /// <see cref="UpdateState.ReadyToRestart"/>; otherwise a no-op.
    /// </summary>
    public void ApplyAndRestart()
    {
        if (PendingUpdate is null || State != UpdateState.ReadyToRestart)
        {
            return;
        }

        _updater.ApplyAndRestart(PendingUpdate);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
