using System.Diagnostics;
using System.Text;

namespace Hermes.Client.Core.Git;

/// <summary>Raw result of one git process invocation.</summary>
public sealed record GitRunResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs git as a child process. The seam tests fake; production gets
/// <see cref="GitProcess"/>.
/// </summary>
public interface IGitRunner
{
    bool IsAvailable { get; }

    Task<GitRunResult> RunAsync(
        IReadOnlyList<string> args,
        string workingDirectory,
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// The real git CLI runner. Finds <c>git</c> on PATH (honest
/// <see cref="GitNotInstalledException"/> when absent — git state is never
/// faked), passes arguments via <see cref="ProcessStartInfo.ArgumentList"/>
/// (no shell interpolation), sets <c>GIT_TERMINAL_PROMPT=0</c> so auth
/// failures surface as stderr instead of hanging on a prompt, streams
/// stderr to the progress reporter, and kills the process on timeout or
/// cancellation so a hung git can never wedge the UI.
/// </summary>
public sealed class GitProcess : IGitRunner
{
    private string? _gitExe;
    private bool _resolved;

    public bool IsAvailable => ResolveGitExe() is not null;

    public async Task<GitRunResult> RunAsync(
        IReadOnlyList<string> args,
        string workingDirectory,
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var exe = ResolveGitExe()
            ?? throw new GitNotInstalledException();

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            throw new GitException($"Repository path does not exist: {workingDirectory}");
        }

        var command = string.Join(' ', args);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        // Never let git block on an interactive credential prompt: fail fast
        // with a mappable stderr message instead.
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        var stdErr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (stdErr)
            {
                stdErr.AppendLine(e.Data);
            }

            var cleaned = GitProgressParser.CleanProgressLine(e.Data);
            if (cleaned is not null)
            {
                progress?.Report(cleaned);
            }
        };

        if (!process.Start())
        {
            throw new GitException($"Could not start git {command}.");
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new GitTimeoutException(command, timeout);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }

        var stdOut = await stdOutTask.ConfigureAwait(false);
        string err;
        lock (stdErr)
        {
            err = stdErr.ToString();
        }

        return new GitRunResult(process.ExitCode, stdOut, err);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Best effort: the process is going away regardless.
        }
    }

    private string? ResolveGitExe()
    {
        if (_resolved)
        {
            return _gitExe;
        }

        _resolved = true;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var names = OperatingSystem.IsWindows()
            ? new[] { "git.exe", "git" }
            : new[] { "git" };

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), name);
                    if (File.Exists(candidate))
                    {
                        _gitExe = candidate;
                        return _gitExe;
                    }
                }
                catch (Exception)
                {
                    // A malformed PATH entry is not fatal.
                }
            }
        }

        return null;
    }
}
