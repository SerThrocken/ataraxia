using Xunit;
using Hermes.Client.Core;
using Hermes.Client.Core.Git;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Detection strings are git's own stderr messages, captured from real
/// git 2.43 runs (not-a-repo, terminal-prompts-disabled auth failure,
/// dirty-checkout refusal, merge-conflict report).
/// </summary>
public sealed class GitErrorMapperTests
{
    [Fact]
    public void ZeroExitMapsToNull()
    {
        Assert.Null(GitErrorMapper.Map(0, "status", "anything"));
    }

    [Fact]
    public void NotARepository()
    {
        var error = GitErrorMapper.Map(
            128,
            "status --porcelain=v1 -z -b",
            "fatal: not a git repository (or any parent up to mount point /)\n" +
            "Stopping at filesystem boundary (GIT_DISCOVERY_ACROSS_FILESYSTEM not set).\n");
        var typed = Assert.IsType<GitNotRepositoryException>(error);
        Assert.Contains("not a git repository", typed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthFailureTerminalPromptsDisabled()
    {
        var error = GitErrorMapper.Map(
            128,
            "ls-remote",
            "fatal: could not read Username for 'http://127.0.0.1:18543': terminal prompts disabled\n");
        Assert.IsType<GitAuthException>(error);
    }

    [Theory]
    [InlineData("fatal: Authentication failed for 'https://github.com/x/y.git/'\n")]
    [InlineData("fatal: could not read Password for 'https://x@y': terminal prompts disabled\n")]
    [InlineData("git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository.\n")]
    public void AuthFailureVariants(string stdErr)
    {
        Assert.IsType<GitAuthException>(GitErrorMapper.Map(128, "push --progress", stdErr));
    }

    [Fact]
    public void DirtyTreeOnSwitch()
    {
        var error = GitErrorMapper.Map(
            1,
            "switch feature-a",
            "error: Your local changes to the following files would be overwritten by checkout:\n" +
            "\talpha.txt\n" +
            "Please commit your changes or stash them before you switch branches.\n" +
            "Aborting\n");
        Assert.IsType<GitDirtyTreeException>(error);
    }

    [Fact]
    public void MergeConflict()
    {
        var error = GitErrorMapper.Map(
            1,
            "merge side",
            "Auto-merging f.txt\n" +
            "CONFLICT (content): Merge conflict in f.txt\n" +
            "Automatic merge failed; fix conflicts and then commit the result.\n");
        var typed = Assert.IsType<GitConflictException>(error);
        Assert.Contains("CONFLICT", typed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownFailureKeepsExitCodeAndStderr()
    {
        var error = GitErrorMapper.Map(1, "commit -m x", "some new git error text\n");
        var typed = Assert.IsType<GitCommandException>(error);
        Assert.Equal(1, typed.ExitCode);
        Assert.Contains("some new git error text", typed.Message, StringComparison.Ordinal);
    }
}

public sealed class GitProgressParserTests
{
    [Fact]
    public void LastCarriageReturnSegmentWins()
    {
        Assert.Equal(
            "Writing objects:  45% (9/20)",
            GitProgressParser.CleanProgressLine("Writing objects:  12% (2/20)\rWriting objects:  45% (9/20)\r"));
    }

    [Fact]
    public void PlainLinesPassThrough()
    {
        Assert.Equal(
            "To https://example.com/r.git",
            GitProgressParser.CleanProgressLine("To https://example.com/r.git\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\r")]
    public void EmptyInputYieldsNull(string? raw)
    {
        Assert.Null(GitProgressParser.CleanProgressLine(raw));
    }
}
