namespace Hermes.Client.Core.Git;

/// <summary>
/// Maps a non-zero git exit to a typed exception from the real stderr text.
/// Detection strings are git's own messages, verified against real git 2.43
/// output (not-a-repo, terminal-prompts-disabled auth failure, dirty-checkout
/// refusal, merge-conflict report). Returns null for exit code 0.
/// </summary>
public static class GitErrorMapper
{
    public static GitException? Map(int exitCode, string command, string stdErr)
    {
        if (exitCode == 0)
        {
            return null;
        }

        var err = stdErr ?? string.Empty;

        if (err.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
        {
            return new GitNotRepositoryException(FirstLine(err));
        }

        if (err.Contains("could not read Username", StringComparison.OrdinalIgnoreCase)
            || err.Contains("could not read Password", StringComparison.OrdinalIgnoreCase)
            || err.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase)
            || err.Contains("Invalid username or password", StringComparison.OrdinalIgnoreCase)
            || err.Contains("Permission denied (publickey)", StringComparison.OrdinalIgnoreCase)
            || err.Contains("Invalid credentials", StringComparison.OrdinalIgnoreCase))
        {
            return new GitAuthException(FirstLine(err));
        }

        if (err.Contains("would be overwritten by checkout", StringComparison.OrdinalIgnoreCase))
        {
            return new GitDirtyTreeException(FirstLine(err));
        }

        if (err.Contains("CONFLICT", StringComparison.Ordinal)
            || err.Contains("Automatic merge failed", StringComparison.OrdinalIgnoreCase)
            || err.Contains("fix conflicts and then commit", StringComparison.OrdinalIgnoreCase))
        {
            return new GitConflictException(ConflictLine(err));
        }

        return new GitCommandException(exitCode, command, err.Trim());
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim().TrimEnd('\r');
        return line.Length <= 300 ? line : line[..300] + "…";
    }

    private static string ConflictLine(string text)
    {
        // Surface the CONFLICT line itself, not the "Auto-merging" preamble.
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Contains("CONFLICT", StringComparison.Ordinal))
            {
                return line.Length <= 300 ? line : line[..300] + "…";
            }
        }

        return FirstLine(text);
    }
}
