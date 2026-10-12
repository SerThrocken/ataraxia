namespace Hermes.Client.Core.Git;

/// <summary>
/// Turns git's carriage-return progress chatter (push/pull stderr) into one
/// clean line for the UI. Progress arrives as <c>\r</c>-separated segments
/// inside one stderr "line"; the last non-empty segment wins. Returns null
/// for empty or pure-whitespace input.
/// </summary>
public static class GitProgressParser
{
    public static string? CleanProgressLine(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        string? last = null;
        foreach (var segment in raw.Split('\r'))
        {
            var trimmed = segment.Trim().TrimEnd('\r', '\n');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                last = trimmed;
            }
        }

        return last;
    }
}
