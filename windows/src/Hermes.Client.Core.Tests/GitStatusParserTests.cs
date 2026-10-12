using Xunit;
using Hermes.Client.Core;
using Hermes.Client.Core.Git;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Fixtures are real <c>git status --porcelain=v1 -z -b</c> output captured
/// from git 2.43 (see worker notes); only the branch headers on the
/// conflict/status-composition cases are assembled from separately
/// captured header patterns, which the header tests verify independently.
/// </summary>
public sealed class GitStatusParserTests
{
    // Captured 2026-10-12: repo with staged delete, staged new file,
    // directory rename (path contains a space), staged+unstaged modify,
    // untracked file. Verified via od -c: NUL separators, "XY new\0old\0"
    // rename order.
    private const string BasicFixture =
        "## main\0D  other.txt\0A  staged-new.txt\0" +
        "R  sub dir/spaced name.txt\0sub/spaced name.txt\0" +
        "MM tracked.txt\0?? untracked.txt\0";

    [Fact]
    public void ParsesBranchWithNoUpstream()
    {
        var status = GitStatusParser.Parse(BasicFixture);
        Assert.Equal("main", status.Branch.Branch);
        Assert.Null(status.Branch.Upstream);
        Assert.False(status.Branch.IsDetached);
        Assert.Equal(0, status.Branch.Ahead);
        Assert.Equal(0, status.Branch.Behind);
    }

    [Fact]
    public void ParsesAllEntryKinds()
    {
        var status = GitStatusParser.Parse(BasicFixture);
        Assert.Equal(5, status.Files.Count);

        var deleted = status.Files[0];
        Assert.Equal("other.txt", deleted.Path);
        Assert.Equal('D', deleted.Index);
        Assert.Equal(' ', deleted.Worktree);
        Assert.True(deleted.IsStaged);
        Assert.False(deleted.IsUnstaged);

        var added = status.Files[1];
        Assert.Equal("staged-new.txt", added.Path);
        Assert.Equal("added (staged)", added.Summary);

        var renamed = status.Files[2];
        Assert.Equal("sub dir/spaced name.txt", renamed.Path);
        Assert.Equal("sub/spaced name.txt", renamed.OriginalPath);
        Assert.True(renamed.IsRenamed);
        Assert.Equal("sub/spaced name.txt → sub dir/spaced name.txt", renamed.DisplayPath);

        var modified = status.Files[3];
        Assert.Equal("tracked.txt", modified.Path);
        Assert.True(modified.IsStaged);
        Assert.True(modified.IsUnstaged);
        Assert.Equal("modified (staged) + modified (unstaged)", modified.Summary);

        var untracked = status.Files[4];
        Assert.True(untracked.IsUntracked);
        Assert.False(untracked.IsStaged);
        Assert.Equal("untracked", untracked.Summary);
    }

    [Fact]
    public void CountsStagedUnstagedAndClean()
    {
        var status = GitStatusParser.Parse(BasicFixture);
        Assert.Equal(4, status.StagedCount);
        Assert.Equal(1, status.UnstagedCount);
        Assert.Equal(0, status.ConflictCount);
        Assert.False(status.IsClean);

        Assert.True(GitStatusParser.Parse("## main\0").IsClean);
        Assert.True(GitStatusParser.Parse("").IsClean);
    }

    [Fact]
    public void ParsesAheadBehindHeaders()
    {
        var ahead = GitStatusParser.Parse("## ahead-branch...origin/main [ahead 2]\0");
        Assert.Equal("ahead-branch", ahead.Branch.Branch);
        Assert.Equal("origin/main", ahead.Branch.Upstream);
        Assert.Equal(2, ahead.Branch.Ahead);
        Assert.Equal(0, ahead.Branch.Behind);

        var both = GitStatusParser.Parse("## ahead-branch...origin/main [ahead 2, behind 1]\0");
        Assert.Equal(2, both.Branch.Ahead);
        Assert.Equal(1, both.Branch.Behind);

        var gone = GitStatusParser.Parse("## ahead-branch...origin/main [gone]\0");
        Assert.True(gone.Branch.UpstreamGone);
    }

    [Fact]
    public void ParsesDetachedAndNoCommitsHeaders()
    {
        var detached = GitStatusParser.Parse("## HEAD (no branch)\0");
        Assert.True(detached.Branch.IsDetached);
        Assert.Null(detached.Branch.Branch);
        Assert.Equal("detached HEAD", detached.Branch.Display);

        var fresh = GitStatusParser.Parse("## No commits yet on main\0");
        Assert.True(fresh.Branch.NoCommitsYet);
        Assert.Equal("main", fresh.Branch.Branch);
    }

    [Fact]
    public void ParsesMergeConflictEntries()
    {
        // UU entry captured from a real conflicted merge (git 2.43);
        // the rest of the fixture reuses the verified entry shapes above.
        var status = GitStatusParser.Parse(
            "## conflict-b\0UU tracked.txt\0AA both-added.txt\0DD both-deleted.txt\0");
        Assert.Equal(3, status.ConflictCount);
        Assert.All(status.Files, f => Assert.True(f.IsConflicted));
        Assert.Equal("merge conflict", status.Files[0].Summary);
    }

    [Theory]
    [InlineData('M', 'M', false)]
    [InlineData('A', 'M', false)]
    [InlineData('M', ' ', false)]
    [InlineData(' ', 'A', false)]
    [InlineData('R', ' ', false)]
    [InlineData('?', '?', false)]
    [InlineData('D', 'D', true)]
    [InlineData('A', 'U', true)]
    [InlineData('U', 'D', true)]
    [InlineData('U', 'A', true)]
    [InlineData('D', 'U', true)]
    [InlineData('A', 'A', true)]
    [InlineData('U', 'U', true)]
    public void ConflictDetectionMatchesGitUnmergedSet(char index, char worktree, bool expected)
    {
        var change = new GitFileChange("f.txt", null, index, worktree);
        Assert.Equal(expected, change.IsConflicted);
    }
}
