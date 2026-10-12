using Xunit;
using Hermes.Client.Core;
using Hermes.Client.Core.Git;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Feeds real <c>git diff --no-color --no-ext-diff -U3</c> output (captured
/// from git 2.43) through the existing <see cref="UnifiedDiffParser"/> to
/// prove the git-integration diff path parses genuine git diffs — reusing
/// the established parser rather than adding a second one.
/// </summary>
public sealed class GitDiffParserTests
{
    // Captured 2026-10-12: worktree diff of alpha.txt (context + one
    // deletion + two additions). Note the trailing-newline context line.
    private const string WorktreeDiff =
        "diff --git a/alpha.txt b/alpha.txt\n" +
        "index b3c5a95..624784e 100644\n" +
        "--- a/alpha.txt\n" +
        "+++ b/alpha.txt\n" +
        "@@ -1,5 +1,6 @@\n" +
        " line1\n" +
        "-line2\n" +
        "+line2 CHANGED\n" +
        " line3\n" +
        " line4\n" +
        " line5\n" +
        "+line6 added\n";

    // Captured 2026-10-12: staged diff of a deleted file (git diff --cached).
    private const string CachedDeleteDiff =
        "diff --git a/beta.txt b/beta.txt\n" +
        "deleted file mode 100644\n" +
        "index 988cd72..0000000\n" +
        "--- a/beta.txt\n" +
        "+++ /dev/null\n" +
        "@@ -1,2 +0,0 @@\n" +
        "-gone1\n" +
        "-gone2\n";

    [Fact]
    public void ParsesRealWorktreeDiff()
    {
        var files = UnifiedDiffParser.Parse(WorktreeDiff);
        var file = Assert.Single(files);
        Assert.Equal("a/alpha.txt", file.OldPath);
        Assert.Equal("b/alpha.txt", file.NewPath);

        var hunk = Assert.Single(file.Hunks);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(5, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(6, hunk.NewCount);

        var kinds = hunk.Lines.Select(l => l.Kind).ToList();
        Assert.Equal(
            new[]
            {
                DiffLineKind.Context, DiffLineKind.Deletion, DiffLineKind.Addition,
                DiffLineKind.Context, DiffLineKind.Context, DiffLineKind.Context,
                DiffLineKind.Addition,
            },
            kinds);
        Assert.Equal("line2 CHANGED", hunk.Lines[2].Text);
    }

    [Fact]
    public void ParsesRealCachedDeleteDiff()
    {
        var files = UnifiedDiffParser.Parse(CachedDeleteDiff);
        var file = Assert.Single(files);
        Assert.Equal("/dev/null", file.NewPath);

        var hunk = Assert.Single(file.Hunks);
        Assert.All(hunk.Lines, l => Assert.Equal(DiffLineKind.Deletion, l.Kind));
        Assert.Equal(new[] { "gone1", "gone2" }, hunk.Lines.Select(l => l.Text));
    }

    [Fact]
    public void EmptyDiffYieldsNoFiles()
    {
        Assert.Empty(UnifiedDiffParser.Parse(""));
        Assert.Empty(UnifiedDiffParser.Parse(null));
    }
}
