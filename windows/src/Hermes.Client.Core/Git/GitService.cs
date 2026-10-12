using System.Text.RegularExpressions;

namespace Hermes.Client.Core.Git;

/// <summary>
/// High-level git operations over <see cref="IGitRunner"/>. Every operation
/// shells out to the real git CLI with an explicit argument list; every
/// status, diff, and branch shown in the UI comes from parsing real git
/// output. Local operations get a 30s timeout, network operations 120s.
/// Non-zero exits are mapped to typed exceptions by
/// <see cref="GitErrorMapper"/> — the real stderr is always surfaced.
/// </summary>
public interface IGitService
{
    string RepositoryPath { get; }

    Task<GitStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiffFile>> GetDiffAsync(
        GitDiffScope scope,
        IReadOnlyList<string>? paths = null,
        CancellationToken cancellationToken = default);

    Task StageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    Task UnstageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    Task CommitAsync(string message, CancellationToken cancellationToken = default);

    Task PushAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task PullAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(CancellationToken cancellationToken = default);

    Task SwitchBranchAsync(string branch, CancellationToken cancellationToken = default);

    Task CreateBranchAsync(string name, string? startPoint = null, CancellationToken cancellationToken = default);

    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
}

public sealed partial class GitService : IGitService
{
    public static readonly TimeSpan LocalTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The deterministic branch-list format. NUL field separators, one
    /// branch per line — see <see cref="GitBranchParser"/>.
    /// </summary>
    public const string BranchFormat =
        "%(refname)%00%(refname:short)%00%(HEAD)%00%(upstream:short)%00%(objectname:short)%00%(subject)";

    private readonly IGitRunner _runner;

    public GitService(IGitRunner runner, string repositoryPath)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        RepositoryPath = !string.IsNullOrWhiteSpace(repositoryPath)
            ? repositoryPath
            : throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
    }

    public string RepositoryPath { get; }

    public async Task<GitStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["status", "--porcelain=v1", "-z", "-b"],
            LocalTimeout, null, cancellationToken).ConfigureAwait(false);
        return GitStatusParser.Parse(result.StdOut);
    }

    public async Task<IReadOnlyList<DiffFile>> GetDiffAsync(
        GitDiffScope scope,
        IReadOnlyList<string>? paths = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "diff", "--no-color", "--no-ext-diff", "-U3" };
        if (scope == GitDiffScope.Staged)
        {
            args.Add("--cached");
        }

        if (paths is { Count: > 0 })
        {
            args.Add("--");
            args.AddRange(paths);
        }

        var result = await RunAsync(args, LocalTimeout, null, cancellationToken).ConfigureAwait(false);
        return UnifiedDiffParser.Parse(result.StdOut);
    }

    public async Task StageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        RequirePaths(paths, nameof(paths));
        var args = new List<string> { "add", "--" };
        args.AddRange(paths);
        await RunAsync(args, LocalTimeout, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnstageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        RequirePaths(paths, nameof(paths));
        var args = new List<string> { "reset", "-q", "HEAD", "--" };
        args.AddRange(paths);
        await RunAsync(args, LocalTimeout, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Commit message is required.", nameof(message));
        }

        await RunAsync(
            ["commit", "-m", message],
            LocalTimeout, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task PushAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await RunAsync(
            ["push", "--progress"],
            NetworkTimeout, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task PullAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        await RunAsync(
            ["pull", "--progress"],
            NetworkTimeout, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["for-each-ref", $"--format={BranchFormat}", "refs/heads", "refs/remotes"],
            LocalTimeout, null, cancellationToken).ConfigureAwait(false);
        return GitBranchParser.Parse(result.StdOut);
    }

    public async Task SwitchBranchAsync(string branch, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException("Branch name is required.", nameof(branch));
        }

        await RunAsync(
            ["switch", branch],
            LocalTimeout, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateBranchAsync(string name, string? startPoint = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Branch name is required.", nameof(name));
        }

        var args = new List<string> { "switch", "-c", name };
        if (!string.IsNullOrWhiteSpace(startPoint))
        {
            args.Add(startPoint);
        }

        await RunAsync(args, LocalTimeout, null, cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"^git version (?<version>\S+)")]
    private static partial Regex VersionRegex();

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["--version"],
            LocalTimeout, null, cancellationToken).ConfigureAwait(false);
        var match = VersionRegex().Match(result.StdOut.Trim());
        return match.Success ? match.Groups["version"].Value : result.StdOut.Trim();
    }

    private async Task<GitRunResult> RunAsync(
        IReadOnlyList<string> args,
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var result = await _runner
            .RunAsync(args, RepositoryPath, timeout, progress, cancellationToken)
            .ConfigureAwait(false);
        var error = GitErrorMapper.Map(result.ExitCode, string.Join(' ', args), result.StdErr);
        if (error is not null)
        {
            throw error;
        }

        return result;
    }

    private static void RequirePaths(IReadOnlyList<string>? paths, string paramName)
    {
        if (paths is null || paths.Count == 0)
        {
            throw new ArgumentException("At least one path is required.", paramName);
        }
    }
}
