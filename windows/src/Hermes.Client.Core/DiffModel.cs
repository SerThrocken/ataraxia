using System.Text.RegularExpressions;

namespace Hermes.Client.Core;

/// <summary>One line inside a diff hunk.</summary>
public enum DiffLineKind
{
    Context,
    Addition,
    Deletion,
    /// <summary>Non-diff chrome the gateway renderer may emit (e.g. an omitted-lines summary).</summary>
    Other,
}

public sealed record DiffLine(DiffLineKind Kind, string Text);

public sealed record DiffHunk(
    string Header,
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffLine> Lines);

/// <summary>One file's worth of a unified diff.</summary>
public sealed record DiffFile(
    string OldPath,
    string NewPath,
    IReadOnlyList<DiffHunk> Hunks,
    IReadOnlyList<DiffLine> Notes);

/// <summary>
/// Parses the <c>inline_diff</c> string the gateway attaches to
/// <c>tool.complete</c> events for file-edit tools. The wire format is the
/// gateway's rendered unified diff: an optional review-header line, per-file
/// <c>a/path → b/path</c> headers, <c>@@</c> hunk headers, and
/// <c>+</c>/<c>-</c>/<c> </c> lines — all wrapped in ANSI color codes, which
/// are stripped. Raw <c>---</c>/<c>+++</c> pairs are accepted too.
/// </summary>
public static partial class UnifiedDiffParser
{
    // NOTE: regular (non-verbatim) string so \x1B is the real ESC byte 0x1B.
    // A verbatim @"\x1B" would match four literal characters instead.
    [GeneratedRegex("\x1B\\[[0-9;]*[A-Za-z]")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex(@"^@@\s+-(?<os>\d+)(,(?<oc>\d+))?\s+\+(?<ns>\d+)(,(?<nc>\d+))?\s+@@")]
    private static partial Regex HunkHeaderRegex();

    public static IReadOnlyList<DiffFile> Parse(string? inlineDiff)
    {
        if (string.IsNullOrWhiteSpace(inlineDiff))
        {
            return [];
        }

        var files = new List<DiffFile>();
        List<DiffHunk> hunks = [];
        List<DiffLine> hunkLines = [];
        List<DiffLine> notes = [];
        string? oldPath = null;
        string? newPath = null;
        DiffHunk? current = null;
        var inFile = false;

        void FlushHunk()
        {
            if (current is not null)
            {
                hunks.Add(current with { Lines = hunkLines.ToList() });
                current = null;
                hunkLines = [];
            }
        }

        void FlushFile()
        {
            FlushHunk();
            if (inFile)
            {
                files.Add(new DiffFile(
                    oldPath ?? "a/?", newPath ?? "b/?",
                    hunks.ToList(), notes.ToList()));
            }

            hunks = [];
            notes = [];
            oldPath = newPath = null;
            inFile = false;
        }

        var rawLines = inlineDiff.Split('\n');
        for (var i = 0; i < rawLines.Length; i++)
        {
            var line = AnsiRegex().Replace(rawLines[i], "").TrimEnd('\r');

            // Per-file header rendered by the gateway: "a/path → b/path".
            var arrow = line.IndexOf(" → ", StringComparison.Ordinal);
            if (arrow > 0 && !line.StartsWith("@@", StringComparison.Ordinal))
            {
                FlushFile();
                inFile = true;
                oldPath = line[..arrow].Trim();
                newPath = line[(arrow + 3)..].Trim();
                continue;
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                FlushFile();
                inFile = true;
                oldPath = line[4..].Trim();
                continue;
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                newPath = line[4..].Trim();
                inFile = true;
                continue;
            }

            if (!inFile)
            {
                // Review header or other leading chrome — skip.
                continue;
            }

            var hunk = HunkHeaderRegex().Match(line);
            if (hunk.Success)
            {
                FlushHunk();
                current = new DiffHunk(
                    line,
                    int.Parse(hunk.Groups["os"].Value),
                    hunk.Groups["oc"].Success ? int.Parse(hunk.Groups["oc"].Value) : 1,
                    int.Parse(hunk.Groups["ns"].Value),
                    hunk.Groups["nc"].Success ? int.Parse(hunk.Groups["nc"].Value) : 1,
                    []);
                continue;
            }

            if (current is null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    notes.Add(new DiffLine(DiffLineKind.Other, line));
                }

                continue;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                hunkLines.Add(new DiffLine(DiffLineKind.Addition, line[1..]));
            }
            else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                hunkLines.Add(new DiffLine(DiffLineKind.Deletion, line[1..]));
            }
            else if (line.StartsWith(' '))
            {
                hunkLines.Add(new DiffLine(DiffLineKind.Context, line[1..]));
            }
            else if (line is @"\ No newline at end of file")
            {
                // Marker only; the affected line is already recorded.
            }
            else if (string.IsNullOrEmpty(line))
            {
                // A genuinely empty line inside a hunk is a context line.
                // The empty segment from a trailing newline is not.
                if (i < rawLines.Length - 1)
                {
                    hunkLines.Add(new DiffLine(DiffLineKind.Context, ""));
                }
            }
            else
            {
                notes.Add(new DiffLine(DiffLineKind.Other, line));
            }
        }

        FlushFile();
        return files;
    }
}

/// <summary>
/// Pulls file diffs out of tool-lifecycle SSE events. Data source, verified
/// 2026-10-11:
/// <list type="bullet">
/// <item>The gateway builds <c>inline_diff</c> (rendered unified-diff text) on
/// <c>tool.complete</c> for file-edit tools (tui_gateway/tool_progress.py).</item>
/// <item><b>But the 8642 CLI gateway's HTTP streams do NOT forward it:</b>
/// <c>POST /api/sessions/{id}/chat/stream</c> emits <c>tool.completed</c> with
/// only {message_id, tool_name, preview, args} (verified live), and
/// <c>GET /v1/runs/{run_id}/events</c> emits <c>tool.completed</c> with only
/// {tool, duration, error, preview} (verified in gateway source,
/// api_server_runs.py). Artifacts are binary upload/download — no before/after.
/// </item>
/// </list>
/// So on 8642 today the extractor returns null and the UI shows "no diff" —
/// never fake data. The parser + control are ready for the day the gateway
/// exposes <c>inline_diff</c> on an HTTP stream (or when targeting the desktop
/// serve backend, whose session transport carries the full payload).
/// </summary>
public static class ToolDiffExtractor
{
    public static string? GetToolCallId(SseEvent e) =>
        e.Name is "tool.complete" or "tool.completed" or "tool.start" or "tool.started"
            ? e.GetString("tool_id")
            : null;

    public static string? ExtractInlineDiff(SseEvent e) =>
        e.Name is "tool.complete" or "tool.completed" ? e.GetString("inline_diff") : null;

    public static IReadOnlyList<DiffFile> ExtractFiles(SseEvent e) =>
        UnifiedDiffParser.Parse(ExtractInlineDiff(e));
}

/// <summary>
/// Abstraction the <c>DiffViewer</c> control renders against. The chat
/// viewmodel feeds parsed <c>tool.complete</c> diffs into a
/// <see cref="ToolDiffRegistry"/>; anything else can implement this.
/// </summary>
public interface IDiffSource
{
    Task<IReadOnlyList<DiffFile>> GetDiffAsync(string toolCallId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory tool-call-id → parsed diffs registry. Backs the diff view for
/// the live session; empty when the gateway sent no <c>inline_diff</c>.
/// </summary>
public sealed class ToolDiffRegistry : IDiffSource
{
    private readonly Dictionary<string, IReadOnlyList<DiffFile>> _diffs = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Set(string toolCallId, IReadOnlyList<DiffFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
        ArgumentNullException.ThrowIfNull(files);
        lock (_gate)
        {
            _diffs[toolCallId] = files;
        }
    }

    public void SetFromInlineDiff(string toolCallId, string? inlineDiff) =>
        Set(toolCallId, UnifiedDiffParser.Parse(inlineDiff));

    public Task<IReadOnlyList<DiffFile>> GetDiffAsync(string toolCallId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _diffs.TryGetValue(toolCallId, out var files) ? files : (IReadOnlyList<DiffFile>)[]);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _diffs.Clear();
        }
    }
}
