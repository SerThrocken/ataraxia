using System.Text;

namespace Hermes.Client.Core;

/// <summary>Per-hunk review decision. The gateway exposes no file-apply route
/// (verified 2026-10-11 against gateway 0.21.6), so accept/reject records the
/// reviewer's selection; the real, verifiable action is exporting the
/// accepted hunks as a unified-diff patch (<see cref="PatchExporter"/>).</summary>
public enum HunkReview
{
    Pending,
    Accepted,
    Rejected,
}

/// <summary>
/// Per-hunk accept/reject selection over one parsed <see cref="DiffFile"/>.
/// Plain class (no MVVM dependency in Core); UI listens to
/// <see cref="StateChanged"/>.
/// </summary>
public sealed class FileReview
{
    private readonly HunkReview[] _states;

    public FileReview(DiffFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
        _states = new HunkReview[file.Hunks.Count];
    }

    public DiffFile File { get; }

    public int HunkCount => _states.Length;

    public int AcceptedCount => _states.Count(s => s == HunkReview.Accepted);

    public int RejectedCount => _states.Count(s => s == HunkReview.Rejected);

    public int PendingCount => _states.Count(s => s == HunkReview.Pending);

    public IReadOnlyList<int> AcceptedHunks =>
        _states.Select((s, i) => (s, i)).Where(t => t.s == HunkReview.Accepted).Select(t => t.i).ToList();

    /// <summary>Raised with the hunk index whenever a decision changes.</summary>
    public event EventHandler<int>? StateChanged;

    public HunkReview GetState(int hunkIndex) => _states[CheckIndex(hunkIndex)];

    public void Accept(int hunkIndex) => Set(hunkIndex, HunkReview.Accepted);

    public void Reject(int hunkIndex) => Set(hunkIndex, HunkReview.Rejected);

    /// <summary>Back to undecided.</summary>
    public void Clear(int hunkIndex) => Set(hunkIndex, HunkReview.Pending);

    /// <summary>Pending → Accepted → Rejected → Accepted …</summary>
    public void Toggle(int hunkIndex) =>
        Set(hunkIndex, GetState(hunkIndex) switch
        {
            HunkReview.Pending => HunkReview.Accepted,
            HunkReview.Accepted => HunkReview.Rejected,
            _ => HunkReview.Accepted,
        });

    public void AcceptAll()
    {
        for (var i = 0; i < _states.Length; i++)
        {
            Set(i, HunkReview.Accepted);
        }
    }

    public void RejectAll()
    {
        for (var i = 0; i < _states.Length; i++)
        {
            Set(i, HunkReview.Rejected);
        }
    }

    private int CheckIndex(int hunkIndex)
    {
        if (hunkIndex < 0 || hunkIndex >= _states.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(hunkIndex));
        }

        return hunkIndex;
    }

    private void Set(int hunkIndex, HunkReview state)
    {
        CheckIndex(hunkIndex);
        if (_states[hunkIndex] != state)
        {
            _states[hunkIndex] = state;
            StateChanged?.Invoke(this, hunkIndex);
        }
    }
}

/// <summary>
/// Builds a valid unified-diff patch from the accepted hunks of a review.
/// The output round-trips through <see cref="UnifiedDiffParser"/> and applies
/// with <c>patch</c>/<c>git apply</c>. Rejected and pending hunks are omitted;
/// parser chrome (<see cref="DiffLineKind.Other"/>) is not valid diff and is
/// left out.
/// </summary>
public static class PatchExporter
{
    public static string BuildPatch(DiffFile file, IEnumerable<int> acceptedHunkIndexes)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(acceptedHunkIndexes);

        var accepted = acceptedHunkIndexes
            .Where(i => i >= 0 && i < file.Hunks.Count)
            .Distinct()
            .Order()
            .ToList();
        if (accepted.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        sb.Append("--- ").AppendLine(file.OldPath);
        sb.Append("+++ ").AppendLine(file.NewPath);
        foreach (var i in accepted)
        {
            var hunk = file.Hunks[i];
            sb.AppendLine(hunk.Header);
            foreach (var line in hunk.Lines)
            {
                sb.Append(line.Kind switch
                {
                    DiffLineKind.Addition => '+',
                    DiffLineKind.Deletion => '-',
                    _ => ' ',
                });
                sb.AppendLine(line.Text);
            }
        }

        return sb.ToString();
    }

    /// <summary>Multi-file patch: only files with at least one accepted hunk appear.</summary>
    public static string BuildPatch(IEnumerable<FileReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);

        var sb = new StringBuilder();
        foreach (var review in reviews)
        {
            sb.Append(BuildPatch(review.File, review.AcceptedHunks));
        }

        return sb.ToString();
    }
}
