using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class FileReviewTests
{
    private const string TwoHunkDiff =
        "a/src/app.cs → b/src/app.cs\n" +
        "@@ -1,3 +1,4 @@\n" +
        " line1\n" +
        "-old2\n" +
        "+new2\n" +
        "+added\n" +
        " line3\n" +
        "@@ -10,3 +11,3 @@\n" +
        " line10\n" +
        "-old11\n" +
        "+new11\n" +
        " line12\n";

    private const string SecondFileDiff =
        "a/src/util.cs → b/src/util.cs\n" +
        "@@ -1,2 +1,2 @@\n" +
        " u1\n" +
        "-uold\n" +
        "+unew\n";

    private static DiffFile ParseSingle(string diff) =>
        Assert.Single(UnifiedDiffParser.Parse(diff));

    [Fact]
    public void InitialState_AllHunksPending()
    {
        var review = new FileReview(ParseSingle(TwoHunkDiff));

        Assert.Equal(2, review.HunkCount);
        Assert.Equal(HunkReview.Pending, review.GetState(0));
        Assert.Equal(HunkReview.Pending, review.GetState(1));
        Assert.Equal(2, review.PendingCount);
        Assert.Equal(0, review.AcceptedCount);
        Assert.Equal(0, review.RejectedCount);
    }

    [Fact]
    public void Accept_Reject_Toggle_Transitions()
    {
        var review = new FileReview(ParseSingle(TwoHunkDiff));

        review.Accept(0);
        Assert.Equal(HunkReview.Accepted, review.GetState(0));
        Assert.Equal(1, review.AcceptedCount);
        Assert.Equal(1, review.PendingCount);

        review.Reject(0);
        Assert.Equal(HunkReview.Rejected, review.GetState(0));
        Assert.Equal(1, review.RejectedCount);

        review.Toggle(1); // Pending -> Accepted
        Assert.Equal(HunkReview.Accepted, review.GetState(1));
        review.Toggle(1); // Accepted -> Rejected
        Assert.Equal(HunkReview.Rejected, review.GetState(1));
        review.Toggle(1); // Rejected -> Accepted
        Assert.Equal(HunkReview.Accepted, review.GetState(1));

        review.Clear(0); // back to Pending
        Assert.Equal(HunkReview.Pending, review.GetState(0));
    }

    [Fact]
    public void AcceptAll_RejectAll_SetEveryHunk()
    {
        var review = new FileReview(ParseSingle(TwoHunkDiff));

        review.AcceptAll();
        Assert.Equal(2, review.AcceptedCount);
        Assert.Equal(0, review.PendingCount);
        Assert.Equal(new[] { 0, 1 }, review.AcceptedHunks);

        review.RejectAll();
        Assert.Equal(2, review.RejectedCount);
        Assert.Empty(review.AcceptedHunks);
    }

    [Fact]
    public void HunkIndex_OutOfRange_Throws()
    {
        var review = new FileReview(ParseSingle(TwoHunkDiff));

        Assert.Throws<ArgumentOutOfRangeException>(() => review.Accept(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => review.Accept(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => review.GetState(99));
    }

    [Fact]
    public void Export_SingleAcceptedHunk_ContainsOnlyThatHunk()
    {
        var file = ParseSingle(TwoHunkDiff);
        var patch = PatchExporter.BuildPatch(file, [0]);

        Assert.Contains("--- a/src/app.cs", patch);
        Assert.Contains("+++ b/src/app.cs", patch);
        Assert.Contains("@@ -1,3 +1,4 @@", patch);
        Assert.DoesNotContain("@@ -10,3 +11,3 @@", patch);
        Assert.Contains("+new2", patch);
        Assert.DoesNotContain("+new11", patch);
    }

    [Fact]
    public void Export_RoundTrips_ThroughParser()
    {
        var file = ParseSingle(TwoHunkDiff);
        var patch = PatchExporter.BuildPatch(file, [1]);

        var reparsed = Assert.Single(UnifiedDiffParser.Parse(patch));
        Assert.Equal("a/src/app.cs", reparsed.OldPath);
        Assert.Equal("b/src/app.cs", reparsed.NewPath);
        var hunk = Assert.Single(reparsed.Hunks);
        Assert.Equal("@@ -10,3 +11,3 @@", hunk.Header);
        Assert.Equal(
            file.Hunks[1].Lines.Select(l => (l.Kind, l.Text)),
            hunk.Lines.Select(l => (l.Kind, l.Text)));
    }

    [Fact]
    public void Export_NoAcceptedHunks_ReturnsEmpty()
    {
        var file = ParseSingle(TwoHunkDiff);

        Assert.Equal("", PatchExporter.BuildPatch(file, []));
        Assert.Equal("", PatchExporter.BuildPatch(file, [5])); // out-of-range index ignored
    }

    [Fact]
    public void Export_MultipleFiles_OnlyAcceptedHunks()
    {
        var files = UnifiedDiffParser.Parse(TwoHunkDiff + SecondFileDiff);
        Assert.Equal(2, files.Count);
        var reviews = files.Select(f => new FileReview(f)).ToList();
        reviews[1].AcceptAll(); // leave the first file fully pending

        var patch = PatchExporter.BuildPatch(reviews);

        Assert.DoesNotContain("a/src/app.cs", patch);
        Assert.Contains("--- a/src/util.cs", patch);
        Assert.Contains("+++ b/src/util.cs", patch);
        Assert.Contains("+unew", patch);
    }

    [Fact]
    public void Export_AllAccepted_PreservesEveryHunk()
    {
        var file = ParseSingle(TwoHunkDiff);
        var patch = PatchExporter.BuildPatch(file, [0, 1]);

        var reparsed = Assert.Single(UnifiedDiffParser.Parse(patch));
        Assert.Equal(2, reparsed.Hunks.Count);
    }

    [Fact]
    public void Export_NullFile_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PatchExporter.BuildPatch(null!, [0]));
        Assert.Throws<ArgumentNullException>(() => new FileReview(null!));
    }
}
