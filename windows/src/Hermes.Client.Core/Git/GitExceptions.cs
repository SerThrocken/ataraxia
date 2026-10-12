namespace Hermes.Client.Core.Git;

/// <summary>Base for all git-integration failures. Messages always carry the
/// real git stderr — nothing is swallowed or paraphrased away.</summary>
public class GitException : Exception
{
    protected GitException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    public GitException(string message)
        : base(message)
    {
    }
}

/// <summary>No <c>git</c> executable found on PATH.</summary>
public sealed class GitNotInstalledException : GitException
{
    public GitNotInstalledException()
        : base("git was not found on PATH. Install Git for Windows (https://git-scm.com) and restart Ataraxia.")
    {
    }
}

/// <summary>The selected folder is not inside a git working tree.</summary>
public sealed class GitNotRepositoryException : GitException
{
    public GitNotRepositoryException(string detail)
        : base($"Not a git repository. {detail}".Trim())
    {
    }
}

/// <summary>Remote rejected the credentials (or none were available).</summary>
public sealed class GitAuthException : GitException
{
    public GitAuthException(string detail)
        : base($"Git authentication failed. {detail}".Trim())
    {
    }
}

/// <summary>A merge/pull/switch hit conflicting changes.</summary>
public sealed class GitConflictException : GitException
{
    public GitConflictException(string detail)
        : base($"Git reported a conflict. {detail}".Trim())
    {
    }
}

/// <summary>A branch switch was refused because of local changes.</summary>
public sealed class GitDirtyTreeException : GitException
{
    public GitDirtyTreeException(string detail)
        : base($"Git refused: local changes would be overwritten. {detail}".Trim())
    {
    }
}

/// <summary>A git invocation ran past its timeout and was killed.</summary>
public sealed class GitTimeoutException : GitException
{
    public GitTimeoutException(string command, TimeSpan timeout)
        : base($"git {command} timed out after {(int)timeout.TotalSeconds}s and was stopped.")
    {
    }
}

/// <summary>Any other non-zero git exit; carries the exit code and full stderr.</summary>
public sealed class GitCommandException : GitException
{
    public GitCommandException(int exitCode, string command, string stdErr)
        : base($"git {command} failed (exit {exitCode}). {stdErr}".Trim())
    {
        ExitCode = exitCode;
        Command = command;
        StdErr = stdErr;
    }

    public int ExitCode { get; }

    public string Command { get; }

    public string StdErr { get; }
}
