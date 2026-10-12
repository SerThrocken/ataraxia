namespace Hermes.Client.Core.Git;

/// <summary>
/// Parses the deterministic <c>git for-each-ref</c> format emitted by
/// <see cref="GitService.GetBranchesAsync"/>:
/// <c>%(refname)%00%(refname:short)%00%(HEAD)%00%(upstream:short)%00%(objectname:short)%00%(subject)</c>
/// with one branch per line (<c>\n</c> terminated). NUL separators keep
/// branch names and subjects with spaces or pipes unambiguous. Verified
/// against real git 2.43 output.
/// </summary>
public static class GitBranchParser
{
    public static IReadOnlyList<GitBranch> Parse(string? stdout)
    {
        var branches = new List<GitBranch>();
        if (string.IsNullOrEmpty(stdout))
        {
            return branches;
        }

        foreach (var line in stdout.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split('\0');
            if (fields.Length < 6)
            {
                continue;
            }

            var fullRef = fields[0];
            branches.Add(new GitBranch(
                FullRef: fullRef,
                Name: fields[1],
                IsCurrent: fields[2] == "*",
                IsRemote: fullRef.StartsWith("refs/remotes/", StringComparison.Ordinal),
                Upstream: string.IsNullOrEmpty(fields[3]) ? null : fields[3],
                ShortSha: fields[4],
                Subject: fields[5]));
        }

        return branches;
    }
}
