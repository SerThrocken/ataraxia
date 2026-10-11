namespace Hermes.Client.Core;

/// <summary>Human relative timestamps for session last-active ("5m ago").</summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset? timestamp)
    {
        if (timestamp is null)
        {
            return "";
        }

        var delta = DateTimeOffset.UtcNow - timestamp.Value.ToUniversalTime();
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        if (delta < TimeSpan.FromDays(7))
        {
            return $"{(int)delta.TotalDays}d ago";
        }

        return timestamp.Value.ToLocalTime().ToString("MMM d");
    }
}
