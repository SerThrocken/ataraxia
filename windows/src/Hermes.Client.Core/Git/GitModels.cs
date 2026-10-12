// Git integration models. All state comes from real git CLI invocations
// parsed by GitStatusParser / GitBranchParser — nothing is invented.
namespace Hermes.Client.Core.Git;

/// <summary>Branch line of <c>git status --porcelain=v1 -b</c>.</summary>
public sealed record GitBranchStatus(
    string? Branch,
    string? Upstream,
    int Ahead,
    int Behind,
    bool IsDetached,
    bool NoCommitsYet,
    bool UpstreamGone)
{
    public string Display =>
        IsDetached ? "detached HEAD"
        : NoCommitsYet ? $"no commits yet on {Branch}"
        : Branch + (Upstream is null ? "" : $" → {Upstream}") +
          (Ahead > 0 || Behind > 0 ? $" [ahead {Ahead}, behind {Behind}]" : "") +
          (UpstreamGone ? " [upstream gone]" : "");
}

/// <summary>
/// One file entry of <c>git status --porcelain=v1 -z</c>. <see cref="Index"/>
/// and <see cref="Worktree"/> are the raw XY status codes; <see cref="Path"/>
/// is the display path, <see cref="OriginalPath"/> the pre-rename source.
/// </summary>
public sealed record GitFileChange(
    string Path,
    string? OriginalPath,
    char Index,
    char Worktree)
{
    public bool IsUntracked => Index == '?' && Worktree == '?';

    public bool IsIgnored => Index == '!' && Worktree == '!';

    public bool IsRenamed => Index == 'R' || Worktree == 'R';

    public bool IsStaged => Index is not (' ' or '?' or '!');

    public bool IsUnstaged => Worktree is not (' ' or '?' or '!');

    /// <summary>
    /// Unmerged paths per git docs: DD, AU, UD, UA, DU, AA, UU —
    /// exactly the pairs where both codes are in {A, D, U}.
    /// </summary>
    public bool IsConflicted =>
        Index is 'A' or 'D' or 'U' && Worktree is 'A' or 'D' or 'U';

    public string DisplayPath =>
        OriginalPath is null ? Path : $"{OriginalPath} → {Path}";

    /// <summary>Short human summary, e.g. "modified, staged + unstaged".</summary>
    public string Summary
    {
        get
        {
            if (IsUntracked)
            {
                return "untracked";
            }

            if (IsConflicted)
            {
                return "merge conflict";
            }

            var parts = new List<string>();
            if (Index != ' ')
            {
                parts.Add(Index switch
                {
                    'M' => "modified (staged)",
                    'A' => "added (staged)",
                    'D' => "deleted (staged)",
                    'R' => "renamed (staged)",
                    'C' => "copied (staged)",
                    'T' => "type changed (staged)",
                    _ => $"index '{Index}'",
                });
            }

            if (Worktree != ' ')
            {
                parts.Add(Worktree switch
                {
                    'M' => "modified (unstaged)",
                    'D' => "deleted (unstaged)",
                    'R' => "renamed (unstaged)",
                    'T' => "type changed (unstaged)",
                    _ => $"worktree '{Worktree}'",
                });
            }

            return parts.Count == 0 ? "unchanged" : string.Join(" + ", parts);
        }
    }
}

/// <summary>Full result of parsing <c>git status --porcelain=v1 -z -b</c>.</summary>
public sealed record GitStatus(
    GitBranchStatus Branch,
    IReadOnlyList<GitFileChange> Files)
{
    public int StagedCount => Files.Count(f => f.IsStaged);

    public int UnstagedCount => Files.Count(f => f.IsUnstaged);

    public int ConflictCount => Files.Count(f => f.IsConflicted);

    public bool IsClean => Files.Count == 0;
}

/// <summary>One branch from <c>git for-each-ref</c>.</summary>
public sealed record GitBranch(
    string FullRef,
    string Name,
    bool IsCurrent,
    bool IsRemote,
    string? Upstream,
    string ShortSha,
    string Subject);

/// <summary>Which side of the diff to read.</summary>
public enum GitDiffScope
{
    Worktree,
    Staged,
}
