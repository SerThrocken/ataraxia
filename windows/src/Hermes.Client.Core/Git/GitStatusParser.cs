using System.Text.RegularExpressions;

namespace Hermes.Client.Core.Git;

/// <summary>
/// Parses <c>git status --porcelain=v1 -z -b</c> output. Verified against
/// real git 2.43 output: the branch header (<c>## …</c>) is NUL-terminated
/// like every entry, and rename entries are <c>XY new\0old\0</c> (new path
/// first, original second).
/// </summary>
public static partial class GitStatusParser
{
    [GeneratedRegex(@"^## (?<rest>.*)$", RegexOptions.Singleline)]
    private static partial Regex BranchHeaderRegex();

    [GeneratedRegex(@"\[(?<inner>[^\]]*)\]$")]
    private static partial Regex AheadBehindRegex();

    [GeneratedRegex(@"ahead (?<ahead>\d+)")]
    private static partial Regex AheadRegex();

    [GeneratedRegex(@"behind (?<behind>\d+)")]
    private static partial Regex BehindRegex();

    public static GitStatus Parse(string? stdout)
    {
        var entries = (stdout ?? string.Empty)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);

        var branch = ParseBranchHeader(entries.Length > 0 ? entries[0] : string.Empty);
        var firstEntry = entries.Length > 0 && entries[0].StartsWith("## ", StringComparison.Ordinal) ? 1 : 0;

        var files = new List<GitFileChange>();
        for (var i = firstEntry; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 4)
            {
                continue;
            }

            var index = entry[0];
            var worktree = entry[1];
            var path = entry[3..];
            string? original = null;

            // Renames and copies carry the original path as the next entry:
            // "XY new\0old\0" (verified against real git output).
            if ((index is 'R' or 'C') && i + 1 < entries.Length)
            {
                original = entries[i + 1];
                i++;
            }

            files.Add(new GitFileChange(path, original, index, worktree));
        }

        return new GitStatus(branch, files);
    }

    private static GitBranchStatus ParseBranchHeader(string header)
    {
        var match = BranchHeaderRegex().Match(header);
        if (!match.Success)
        {
            return new GitBranchStatus(null, null, 0, 0, false, false, false);
        }

        var rest = match.Groups["rest"].Value;

        if (rest.StartsWith("HEAD (no branch)", StringComparison.Ordinal))
        {
            return new GitBranchStatus(null, null, 0, 0, true, false, false);
        }

        if (rest.StartsWith("No commits yet on ", StringComparison.Ordinal))
        {
            return new GitBranchStatus(
                rest["No commits yet on ".Length..], null, 0, 0, false, true, false);
        }

        var ahead = 0;
        var behind = 0;
        var gone = false;
        var ab = AheadBehindRegex().Match(rest);
        if (ab.Success)
        {
            rest = rest[..ab.Index].TrimEnd();
            var inner = ab.Groups["inner"].Value;
            var a = AheadRegex().Match(inner);
            var b = BehindRegex().Match(inner);
            if (a.Success)
            {
                ahead = int.Parse(a.Groups["ahead"].Value);
            }

            if (b.Success)
            {
                behind = int.Parse(b.Groups["behind"].Value);
            }

            gone = inner.Contains("gone", StringComparison.Ordinal);
        }

        // Branch names cannot contain "..", so the "..." separator is unambiguous.
        string? branch = rest;
        string? upstream = null;
        var sep = rest.IndexOf("...", StringComparison.Ordinal);
        if (sep >= 0)
        {
            branch = rest[..sep];
            upstream = rest[(sep + 3)..];
        }

        return new GitBranchStatus(branch, upstream, ahead, behind, false, false, gone);
    }
}
