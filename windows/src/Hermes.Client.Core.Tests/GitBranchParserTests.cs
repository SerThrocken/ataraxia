using Xunit;
using Hermes.Client.Core;
using Hermes.Client.Core.Git;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Fixture is real <c>git for-each-ref</c> output captured from git 2.43
/// using the exact format string <see cref="GitService.BranchFormat"/>
/// (verified byte-for-byte with od -c): NUL-separated fields, one branch
/// per <c>\n</c>-terminated line, <c>%(HEAD)</c> rendering a space for
/// non-current branches.
/// </summary>
public sealed class GitBranchParserTests
{
    private const string Fixture =
        "refs/heads/ahead-branch\0ahead-branch\0 \0origin/main\031b7198\0one more ahead\n" +
        "refs/heads/feature-a\0feature-a\0 \0\07eaef9a\0feature A\n" +
        "refs/heads/main\0main\0*\0origin/main\0f9b113b\0remote moves\n" +
        "refs/remotes/origin/main\0origin/main\0 \0\0f9b113b\0remote moves\n";

    [Fact]
    public void ParsesLocalBranches()
    {
        var branches = GitBranchParser.Parse(Fixture);
        Assert.Equal(4, branches.Count);

        var ahead = branches[0];
        Assert.Equal("refs/heads/ahead-branch", ahead.FullRef);
        Assert.Equal("ahead-branch", ahead.Name);
        Assert.False(ahead.IsCurrent);
        Assert.False(ahead.IsRemote);
        Assert.Equal("origin/main", ahead.Upstream);
        Assert.Equal("31b7198", ahead.ShortSha);
        Assert.Equal("one more ahead", ahead.Subject);

        var feature = branches[1];
        Assert.Null(feature.Upstream);
        Assert.Equal("7eaef9a", feature.ShortSha);
    }

    [Fact]
    public void MarksCurrentBranch()
    {
        var branches = GitBranchParser.Parse(Fixture);
        var current = Assert.Single(branches, b => b.IsCurrent);
        Assert.Equal("main", current.Name);
        Assert.Equal("origin/main", current.Upstream);
    }

    [Fact]
    public void DetectsRemoteBranchesByFullRef()
    {
        var branches = GitBranchParser.Parse(Fixture);
        var remote = Assert.Single(branches, b => b.IsRemote);
        Assert.Equal("origin/main", remote.Name);
        Assert.Equal("refs/remotes/origin/main", remote.FullRef);
        Assert.False(remote.IsCurrent);
    }

    [Fact]
    public void EmptyAndGarbageInputYieldNoBranches()
    {
        Assert.Empty(GitBranchParser.Parse(null));
        Assert.Empty(GitBranchParser.Parse(""));
        Assert.Empty(GitBranchParser.Parse("not-a-branch-line\n"));
    }
}
