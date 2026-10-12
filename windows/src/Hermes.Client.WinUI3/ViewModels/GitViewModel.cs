using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Hermes.Client.Core.Git;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Git integration page view model. All git state flows through
/// <see cref="IGitService"/> (real git CLI via <see cref="GitProcess"/>);
/// nothing here invents repository state. Long operations are cancellable
/// and every git call has a timeout inside the runner, so a hung git can
/// never wedge the UI.
/// </summary>
public sealed partial class GitViewModel : ObservableObject
{
    private readonly IGitRunner _runner;
    private GitService? _git;
    private CancellationTokenSource? _operationCts;

    public GitViewModel(IGitRunner runner)
    {
        _runner = runner;
        GitAvailable = runner.IsAvailable;
    }

    public bool GitAvailable { get; }

    public bool GitMissing => !GitAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    public partial string RepoPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GitVersion { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GitBranchStatus? BranchStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    [NotifyPropertyChangedFor(nameof(CanCreateBranch))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string? ProgressText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    public partial string CommitMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateBranch))]
    public partial string NewBranchName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))]
    public partial GitBranch? SelectedBranch { get; set; }

    public bool HasError => Error is not null;

    public bool CanOperate => GitAvailable && !IsBusy && _git is not null;

    /// <summary>Switching to a remote-tracking ref is refused by git.</summary>
    public bool CanSwitchBranch =>
        CanOperate && SelectedBranch is { IsRemote: false, IsCurrent: false };

    public bool CanCommit => CanOperate && !string.IsNullOrWhiteSpace(CommitMessage);

    public bool CanCreateBranch => CanOperate && !string.IsNullOrWhiteSpace(NewBranchName);

    public ObservableCollection<GitFileRow> Files { get; } = new();

    public ObservableCollection<GitBranch> Branches { get; } = new();

    public ObservableCollection<DiffFile> DiffFiles { get; } = new();

    public string BranchDisplay => BranchStatus?.Display ?? "—";

    public string ScreenReaderStatus =>
        !GitAvailable ? "Git is not installed or not on PATH."
        : _git is null ? "No repository selected."
        : BranchStatus is null ? "Repository status not loaded."
        : $"{BranchDisplay}. {Files.Count} changed files, " +
          $"{Files.Count(f => f.Change.IsConflicted)} conflicts.";

    /// <summary>Called by the page after the folder picker returns.</summary>
    public async Task OpenRepositoryAsync(string path)
    {
        _git = new GitService(_runner, path);
        RepoPath = path;
        DiffFiles.Clear();
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_git is null || IsBusy)
        {
            return;
        }

        await RunGuardedAsync("refresh", async ct =>
        {
            var statusTask = _git.GetStatusAsync(ct);
            var branchesTask = _git.GetBranchesAsync(ct);
            var versionTask = _git.GetVersionAsync(ct);

            var status = await statusTask;
            var branches = await branchesTask;
            GitVersion = await versionTask;

            BranchStatus = status.Branch;
            Files.Clear();
            foreach (var file in status.Files)
            {
                Files.Add(new GitFileRow(file));
            }

            Branches.Clear();
            foreach (var branch in branches)
            {
                Branches.Add(branch);
            }

            SelectedBranch = Branches.FirstOrDefault(b => b.IsCurrent)
                ?? Branches.FirstOrDefault();

            OnPropertyChanged(nameof(BranchDisplay));
            OnPropertyChanged(nameof(ScreenReaderStatus));
        });
    }

    [RelayCommand]
    public async Task ShowDiffAsync(GitFileRow? row)
    {
        if (_git is null || row is null || IsBusy)
        {
            return;
        }

        await RunGuardedAsync("diff", async ct =>
        {
            // A file with unstaged changes shows the worktree diff; a
            // staged-only file shows the staged diff.
            var scope = row.Change.IsUnstaged ? GitDiffScope.Worktree : GitDiffScope.Staged;
            var files = await _git.GetDiffAsync(scope, [row.Change.Path], ct);
            DiffFiles.Clear();
            foreach (var file in files)
            {
                DiffFiles.Add(file);
            }
        });
    }

    [RelayCommand]
    public async Task StageSelectedAsync()
    {
        var paths = Files.Where(f => f.IsSelected).Select(f => f.Change.Path).ToList();
        if (paths.Count == 0 || _git is null || IsBusy)
        {
            return;
        }

        await RunGuardedAsync("stage", async ct =>
        {
            await _git.StageAsync(paths, ct);
            await RefreshCoreAsync(ct);
        });
    }

    [RelayCommand]
    public async Task UnstageSelectedAsync()
    {
        var paths = Files.Where(f => f.IsSelected).Select(f => f.Change.Path).ToList();
        if (paths.Count == 0 || _git is null || IsBusy)
        {
            return;
        }

        await RunGuardedAsync("unstage", async ct =>
        {
            await _git.UnstageAsync(paths, ct);
            await RefreshCoreAsync(ct);
        });
    }

    [RelayCommand]
    public async Task CommitAsync()
    {
        if (_git is null || IsBusy || string.IsNullOrWhiteSpace(CommitMessage))
        {
            return;
        }

        await RunGuardedAsync("commit", async ct =>
        {
            await _git.CommitAsync(CommitMessage.Trim(), ct);
            CommitMessage = string.Empty;
            await RefreshCoreAsync(ct);
        });
    }

    [RelayCommand]
    public async Task PushAsync()
    {
        if (_git is null || IsBusy)
        {
            return;
        }

        await RunGuardedAsync("push", async (ct, progress) =>
        {
            await _git.PushAsync(progress, ct);
            await RefreshCoreAsync(ct);
        }, reportProgress: true);
    }

    [RelayCommand]
    public async Task PullAsync()
    {
        if (_git is null || IsBusy)
        {
            return;
        }

        await RunGuardedAsync("pull", async (ct, progress) =>
        {
            await _git.PullAsync(progress, ct);
            await RefreshCoreAsync(ct);
        }, reportProgress: true);
    }

    [RelayCommand]
    public async Task SwitchBranchAsync()
    {
        if (_git is null || IsBusy || SelectedBranch is null || SelectedBranch.IsRemote)
        {
            return;
        }

        await RunGuardedAsync("switch", async ct =>
        {
            await _git.SwitchBranchAsync(SelectedBranch.Name, ct);
            await RefreshCoreAsync(ct);
        });
    }

    [RelayCommand]
    public async Task CreateBranchAsync()
    {
        if (_git is null || IsBusy || string.IsNullOrWhiteSpace(NewBranchName))
        {
            return;
        }

        await RunGuardedAsync("create branch", async ct =>
        {
            await _git.CreateBranchAsync(NewBranchName.Trim(), null, ct);
            NewBranchName = string.Empty;
            await RefreshCoreAsync(ct);
        });
    }

    [RelayCommand]
    public void Cancel()
    {
        // The guarded runner may dispose the CTS in its finally block;
        // a click landing in that window must not throw.
        var cts = Volatile.Read(ref _operationCts);
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        var status = await _git!.GetStatusAsync(ct);
        var branches = await _git.GetBranchesAsync(ct);
        BranchStatus = status.Branch;
        Files.Clear();
        foreach (var file in status.Files)
        {
            Files.Add(new GitFileRow(file));
        }

        var current = SelectedBranch?.Name;
        Branches.Clear();
        foreach (var branch in branches)
        {
            Branches.Add(branch);
        }

        SelectedBranch = Branches.FirstOrDefault(b => b.IsCurrent)
            ?? Branches.FirstOrDefault(b => b.Name == current);
        DiffFiles.Clear();
        OnPropertyChanged(nameof(BranchDisplay));
        OnPropertyChanged(nameof(ScreenReaderStatus));
    }

    private async Task RunGuardedAsync(
        string operation,
        Func<CancellationToken, Task> work,
        bool reportProgress = false)
    {
        await RunGuardedAsync(operation,
            (ct, _) => work(ct),
            reportProgress);
    }

    private async Task RunGuardedAsync(
        string operation,
        Func<CancellationToken, IProgress<string>, Task> work,
        bool reportProgress = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        ProgressText = null;
        _operationCts = new CancellationTokenSource();
        try
        {
            IProgress<string> progress = new Progress<string>(text =>
            {
                if (reportProgress)
                {
                    ProgressText = text;
                }
            });
            await work(_operationCts.Token, progress);
        }
        catch (OperationCanceledException)
        {
            ProgressText = $"{operation} cancelled.";
        }
        catch (GitException ex)
        {
            // Typed mapping already carries the real git stderr.
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = $"Git {operation} failed unexpectedly: {Trim(ex.Message)}";
        }
        finally
        {
            _operationCts.Dispose();
            _operationCts = null;
            IsBusy = false;
        }
    }

    private static string Trim(string? value, int maxLength = 400) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
