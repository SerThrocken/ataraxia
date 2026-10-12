using Xunit;
using Hermes.Client.Core;
using Hermes.Client.Core.Git;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Verifies the exact git argument lists <see cref="GitService"/> builds,
/// using a recording <see cref="IGitRunner"/> — no git process is launched.
/// </summary>
public sealed class GitCommandConstructionTests
{
    private sealed class RecordingRunner : IGitRunner
    {
        public bool IsAvailable => true;

        public List<string[]> Calls { get; } = new();

        public List<TimeSpan> Timeouts { get; } = new();

        public GitRunResult Result { get; set; } = new(0, string.Empty, string.Empty);

        public Task<GitRunResult> RunAsync(
            IReadOnlyList<string> args,
            string workingDirectory,
            TimeSpan timeout,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            Calls.Add(args.ToArray());
            Timeouts.Add(timeout);
            return Task.FromResult(Result);
        }
    }

    private static (GitService Service, RecordingRunner Runner) Create()
    {
        var runner = new RecordingRunner();
        return (new GitService(runner, @"C:\repo"), runner);
    }

    [Fact]
    public async Task StatusUsesPorcelainV1ZB()
    {
        var (service, runner) = Create();
        await service.GetStatusAsync();
        Assert.Equal(
            new[] { "status", "--porcelain=v1", "-z", "-b" },
            Assert.Single(runner.Calls));
    }

    [Fact]
    public async Task DiffWorktreeAndStaged()
    {
        var (service, runner) = Create();
        await service.GetDiffAsync(GitDiffScope.Worktree, ["a.txt"]);
        Assert.Equal(
            new[] { "diff", "--no-color", "--no-ext-diff", "-U3", "--", "a.txt" },
            runner.Calls[0]);

        await service.GetDiffAsync(GitDiffScope.Staged);
        Assert.Equal(
            new[] { "diff", "--no-color", "--no-ext-diff", "-U3", "--cached" },
            runner.Calls[1]);
    }

    [Fact]
    public async Task StageUnstageCommit()
    {
        var (service, runner) = Create();
        await service.StageAsync(["a.txt", "b.txt"]);
        await service.UnstageAsync(["a.txt"]);
        await service.CommitAsync("a message");
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(new[] { "add", "--", "a.txt", "b.txt" }, runner.Calls[0]);
        Assert.Equal(new[] { "reset", "-q", "HEAD", "--", "a.txt" }, runner.Calls[1]);
        Assert.Equal(new[] { "commit", "-m", "a message" }, runner.Calls[2]);
    }

    [Fact]
    public async Task PushPullUseProgressFlagAndNetworkTimeout()
    {
        var (service, runner) = Create();
        await service.PushAsync();
        await service.PullAsync();
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(new[] { "push", "--progress" }, runner.Calls[0]);
        Assert.Equal(new[] { "pull", "--progress" }, runner.Calls[1]);
        Assert.All(runner.Timeouts, t => Assert.Equal(GitService.NetworkTimeout, t));
    }

    [Fact]
    public async Task BranchesUsesForEachRefWithServiceFormat()
    {
        var (service, runner) = Create();
        runner.Result = new GitRunResult(0, "", "");
        await service.GetBranchesAsync();
        Assert.Equal(
            new[] { "for-each-ref", $"--format={GitService.BranchFormat}", "refs/heads", "refs/remotes" },
            Assert.Single(runner.Calls));
    }

    [Fact]
    public async Task SwitchAndCreateBranch()
    {
        var (service, runner) = Create();
        await service.SwitchBranchAsync("main");
        await service.CreateBranchAsync("feature");
        await service.CreateBranchAsync("hotfix", "origin/main");
        Assert.Equal(3, runner.Calls.Count);
        Assert.Equal(new[] { "switch", "main" }, runner.Calls[0]);
        Assert.Equal(new[] { "switch", "-c", "feature" }, runner.Calls[1]);
        Assert.Equal(new[] { "switch", "-c", "hotfix", "origin/main" }, runner.Calls[2]);
    }

    [Fact]
    public async Task VersionParsesRealGitVersionOutput()
    {
        var (service, runner) = Create();
        runner.Result = new GitRunResult(0, "git version 2.43.0\n", "");
        Assert.Equal("2.43.0", await service.GetVersionAsync());
        Assert.Equal(new[] { "--version" }, Assert.Single(runner.Calls));
    }

    [Fact]
    public async Task NonZeroExitThrowsMappedException()
    {
        var (service, runner) = Create();
        runner.Result = new GitRunResult(
            128, "", "fatal: not a git repository (or any parent up to mount point /)\n");
        await Assert.ThrowsAsync<GitNotRepositoryException>(() => service.GetStatusAsync());
    }

    [Fact]
    public async Task EmptyInputsAreRejectedBeforeRunningGit()
    {
        var (service, runner) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => service.StageAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UnstageAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CommitAsync("  "));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SwitchBranchAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateBranchAsync(""));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void ConstructorRequiresRunnerAndPath()
    {
        Assert.Throws<ArgumentNullException>(() => new GitService(null!, @"C:\repo"));
        Assert.Throws<ArgumentException>(() => new GitService(new RecordingRunner(), ""));
    }
}
