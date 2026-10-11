using System.Text.Json;
using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class UnifiedDiffParserTests
{
    // Mirrors the gateway's rendered wire format (agent/display.py):
    // review-header line, "a/path → b/path" file headers, @@ hunks,
    // all wrapped in ANSI color codes.
    private const string WireDiff =
        "\x1b[1mReview the changes below:\x1b[0m\n" +
        "\x1b[36ma/src/notes.txt → b/src/notes.txt\x1b[0m\n" +
        "\x1b[35m@@ -1,3 +1,4 @@\x1b[0m\n" +
        " first\n" +
        "\x1b[31m-second\x1b[0m\n" +
        "\x1b[32m+second!\x1b[0m\n" +
        "\x1b[32m+brand new\x1b[0m\n" +
        " third\n" +
        "\x1b[36ma/src/other.txt → b/src/other.txt\x1b[0m\n" +
        "\x1b[35m@@ -10 +10 @@\x1b[0m\n" +
        "\x1b[31m-old\x1b[0m\n" +
        "\x1b[32m+new\x1b[0m\n";

    [Fact]
    public void ParsesGatewayWireFormat()
    {
        var files = UnifiedDiffParser.Parse(WireDiff);
        Assert.Equal(2, files.Count);

        var first = files[0];
        Assert.Equal("a/src/notes.txt", first.OldPath);
        Assert.Equal("b/src/notes.txt", first.NewPath);
        Assert.Single(first.Hunks);
        var hunk = first.Hunks[0];
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(3, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(4, hunk.NewCount);
        Assert.Equal(
            new[]
            {
                DiffLineKind.Context, DiffLineKind.Deletion,
                DiffLineKind.Addition, DiffLineKind.Addition, DiffLineKind.Context,
            },
            hunk.Lines.Select(l => l.Kind));
        Assert.Equal("second", hunk.Lines[1].Text);
        Assert.Equal("second!", hunk.Lines[2].Text);

        var second = files[1];
        Assert.Equal("a/src/other.txt", second.OldPath);
        Assert.Single(second.Hunks);
        Assert.Equal(2, second.Hunks[0].Lines.Count);
    }

    [Fact]
    public void AnsiCodesAreStrippedEverywhere()
    {
        var files = UnifiedDiffParser.Parse(WireDiff);
        // Char-based check: xUnit's string DoesNotContain misreports lone
        // control characters (verified 2026-10-11: it claimed U+001B at pos 0
        // of "first", whose bytes contain no 0x1B).
        const char esc = '\x1b';
        Assert.All(files.SelectMany(f => f.Hunks).SelectMany(h => h.Lines),
            l => Assert.False(l.Text.Contains(esc), "ESC in line: " + l.Text));
        Assert.All(files, f => Assert.False(f.NewPath.Contains(esc), "ESC in path: " + f.NewPath));
    }

    [Fact]
    public void ReviewHeaderIsSkippedNotParsed()
    {
        var files = UnifiedDiffParser.Parse(WireDiff);
        Assert.DoesNotContain(files.SelectMany(f => f.Notes), n => n.Text.Contains("Review"));
    }

    [Fact]
    public void RawUnifiedDiffHeadersAlsoParse()
    {
        const string raw = "--- a/x.txt\n+++ b/x.txt\n@@ -1 +1 @@\n-a\n+b\n";
        var files = UnifiedDiffParser.Parse(raw);
        var file = Assert.Single(files);
        Assert.Equal("a/x.txt", file.OldPath);
        Assert.Equal("b/x.txt", file.NewPath);
        Assert.Equal(DiffLineKind.Deletion, file.Hunks[0].Lines[0].Kind);
    }

    [Fact]
    public void EmptyAndNullYieldNoFiles()
    {
        Assert.Empty(UnifiedDiffParser.Parse(null));
        Assert.Empty(UnifiedDiffParser.Parse(""));
        Assert.Empty(UnifiedDiffParser.Parse("   "));
    }

    [Fact]
    public void NonDiffTextYieldsNoFiles()
    {
        Assert.Empty(UnifiedDiffParser.Parse("just a chat message\nno diff here"));
    }

    private static SseEvent ToolCompleteEvent(string inlineDiff)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            tool_id = "call_1",
            name = "write_file",
            inline_diff = inlineDiff,
        }));
        return new SseEvent("tool.complete", doc, "sess_1", "run_1", 7, 0);
    }

    [Fact]
    public void ExtractorPullsInlineDiffFromToolComplete()
    {
        using var e = ToolCompleteEvent(WireDiff);
        Assert.Equal("call_1", ToolDiffExtractor.GetToolCallId(e));
        Assert.Equal(WireDiff, ToolDiffExtractor.ExtractInlineDiff(e));
        Assert.Equal(2, ToolDiffExtractor.ExtractFiles(e).Count);
    }

    [Fact]
    public void ExtractorIgnoresOtherEvents()
    {
        using var doc = JsonDocument.Parse("""{"delta":"hi"}""");
        using var e = new SseEvent("assistant.delta", doc, "s", "r", 1, 0);
        Assert.Null(ToolDiffExtractor.ExtractInlineDiff(e));
        Assert.Empty(ToolDiffExtractor.ExtractFiles(e));
    }

    [Fact]
    public async Task RegistryRoundTripsByToolCallId()
    {
        var registry = new ToolDiffRegistry();
        registry.SetFromInlineDiff("call_1", WireDiff);
        var files = await registry.GetDiffAsync("call_1");
        Assert.Equal(2, files.Count);
        Assert.Empty(await registry.GetDiffAsync("unknown-call"));
    }
}
