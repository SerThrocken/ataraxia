using Hermes.Client.Core;
using Xunit;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Tests for <see cref="UpdateCoordinator"/>: the UI-agnostic self-update
/// state machine. The <see cref="IAppUpdater"/> backend is a fake here; the
/// real Velopack backend lives in the WinUI3 app.
/// </summary>
public sealed class UpdateCoordinatorTests
{
    private sealed class FakeUpdater : IAppUpdater
    {
        public string CurrentVersion { get; set; } = "1.0.0";
        public bool IsFeedConfigured { get; set; } = true;
        public Func<CancellationToken, Task<AppUpdateInfo?>>? OnCheck { get; set; }
        public Func<AppUpdateInfo, IProgress<double>, CancellationToken, Task>? OnDownload { get; set; }
        public Action<AppUpdateInfo>? OnApply { get; set; }
        public bool CheckCalled { get; private set; }
        public bool DownloadCalled { get; private set; }

        public Task<AppUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            CheckCalled = true;
            return OnCheck is null ? Task.FromResult<AppUpdateInfo?>(null) : OnCheck(cancellationToken);
        }

        public Task DownloadAsync(AppUpdateInfo update, IProgress<double> progress, CancellationToken cancellationToken = default)
        {
            DownloadCalled = true;
            return OnDownload is null ? Task.CompletedTask : OnDownload(update, progress, cancellationToken);
        }

        public void ApplyAndRestart(AppUpdateInfo update) => OnApply?.Invoke(update);
    }

    [Fact]
    public void Ctor_NoFeedConfigured_StateIsNotConfigured()
    {
        var coordinator = new UpdateCoordinator(new FakeUpdater { IsFeedConfigured = false });

        Assert.Equal(UpdateState.NotConfigured, coordinator.State);
        Assert.False(coordinator.CanCheck);
        Assert.Contains("No update feed", coordinator.Message);
    }

    [Fact]
    public void Ctor_FeedConfigured_StateIsIdle()
    {
        var coordinator = new UpdateCoordinator(new FakeUpdater());

        Assert.Equal(UpdateState.Idle, coordinator.State);
        Assert.True(coordinator.CanCheck);
        Assert.Equal("1.0.0", coordinator.CurrentVersion);
    }

    [Fact]
    public async Task Check_NoUpdateAvailable_StateIsUpToDate()
    {
        var coordinator = new UpdateCoordinator(new FakeUpdater());

        await coordinator.CheckForUpdatesAsync();

        Assert.Equal(UpdateState.UpToDate, coordinator.State);
        Assert.Contains("1.0.0", coordinator.Message);
    }

    [Fact]
    public async Task Check_UpdateFound_StateIsUpdateAvailable()
    {
        var updater = new FakeUpdater { OnCheck = _ => Task.FromResult<AppUpdateInfo?>(new AppUpdateInfo("1.2.0")) };
        var coordinator = new UpdateCoordinator(updater);

        await coordinator.CheckForUpdatesAsync();

        Assert.Equal(UpdateState.UpdateAvailable, coordinator.State);
        Assert.Equal("1.2.0", coordinator.PendingUpdate?.Version);
        Assert.Contains("1.2.0", coordinator.Message);
        Assert.True(coordinator.CanDownload);
        Assert.False(coordinator.CanApply);
    }

    [Fact]
    public async Task Check_NotInstalled_StateIsNotInstalled()
    {
        var updater = new FakeUpdater { OnCheck = _ => throw new AppNotInstalledException("dev build") };
        var coordinator = new UpdateCoordinator(updater);

        await coordinator.CheckForUpdatesAsync();

        Assert.Equal(UpdateState.NotInstalled, coordinator.State);
        Assert.Contains("dev/debug build", coordinator.Message);
    }

    [Fact]
    public async Task Check_Error_StateIsErrorWithMessage()
    {
        var updater = new FakeUpdater { OnCheck = _ => throw new InvalidOperationException("feed exploded") };
        var coordinator = new UpdateCoordinator(updater);

        await coordinator.CheckForUpdatesAsync();

        Assert.Equal(UpdateState.Error, coordinator.State);
        Assert.Contains("feed exploded", coordinator.Message);
    }

    [Fact]
    public async Task Check_NoFeedConfigured_DoesNotCallBackend()
    {
        var updater = new FakeUpdater { IsFeedConfigured = false };
        var coordinator = new UpdateCoordinator(updater);

        await coordinator.CheckForUpdatesAsync();

        Assert.False(updater.CheckCalled);
        Assert.Equal(UpdateState.NotConfigured, coordinator.State);
    }

    [Fact]
    public async Task Download_ReportsProgressAndBecomesReadyToRestart()
    {
        var progressSeen = new List<double>();
        var updater = new FakeUpdater
        {
            OnCheck = _ => Task.FromResult<AppUpdateInfo?>(new AppUpdateInfo("1.2.0")),
            OnDownload = (_, progress, _) =>
            {
                progress.Report(0.5);
                return Task.CompletedTask;
            },
        };
        var coordinator = new UpdateCoordinator(updater);
        coordinator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateCoordinator.Progress))
            {
                progressSeen.Add(coordinator.Progress);
            }
        };
        await coordinator.CheckForUpdatesAsync();

        await coordinator.DownloadAsync();

        Assert.Contains(0.5, progressSeen);
        Assert.Equal(UpdateState.ReadyToRestart, coordinator.State);
        Assert.True(coordinator.CanApply);
        Assert.Contains("1.2.0", coordinator.Message);
    }

    [Fact]
    public async Task Download_Failure_StateIsErrorWithMessage()
    {
        var updater = new FakeUpdater
        {
            OnCheck = _ => Task.FromResult<AppUpdateInfo?>(new AppUpdateInfo("1.2.0")),
            OnDownload = (_, _, _) => throw new InvalidOperationException("checksum mismatch"),
        };
        var coordinator = new UpdateCoordinator(updater);
        await coordinator.CheckForUpdatesAsync();

        await coordinator.DownloadAsync();

        Assert.Equal(UpdateState.Error, coordinator.State);
        Assert.Contains("checksum mismatch", coordinator.Message);
    }

    [Fact]
    public async Task Download_WithoutPendingUpdate_DoesNothing()
    {
        var updater = new FakeUpdater();
        var coordinator = new UpdateCoordinator(updater);

        await coordinator.DownloadAsync();

        Assert.False(updater.DownloadCalled);
        Assert.Equal(UpdateState.Idle, coordinator.State);
    }

    [Fact]
    public async Task Apply_AfterDownload_CallsBackendWithPendingUpdate()
    {
        AppUpdateInfo? applied = null;
        var updater = new FakeUpdater
        {
            OnCheck = _ => Task.FromResult<AppUpdateInfo?>(new AppUpdateInfo("1.2.0")),
            OnApply = update => applied = update,
        };
        var coordinator = new UpdateCoordinator(updater);
        await coordinator.CheckForUpdatesAsync();
        await coordinator.DownloadAsync();

        coordinator.ApplyAndRestart();

        Assert.Equal("1.2.0", applied?.Version);
    }

    [Fact]
    public void Apply_WithoutPendingUpdate_DoesNothing()
    {
        var applied = false;
        var updater = new FakeUpdater { OnApply = _ => applied = true };
        var coordinator = new UpdateCoordinator(updater);

        coordinator.ApplyAndRestart();

        Assert.False(applied);
    }

    [Fact]
    public async Task CanCheck_FalseWhileBusy()
    {
        var tcs = new TaskCompletionSource<AppUpdateInfo?>();
        var updater = new FakeUpdater { OnCheck = _ => tcs.Task };
        var coordinator = new UpdateCoordinator(updater);

        var check = coordinator.CheckForUpdatesAsync();

        Assert.Equal(UpdateState.Checking, coordinator.State);
        Assert.True(coordinator.IsBusy);
        Assert.False(coordinator.CanCheck);

        tcs.SetResult(null);
        await check;
        Assert.False(coordinator.IsBusy);
    }
}
