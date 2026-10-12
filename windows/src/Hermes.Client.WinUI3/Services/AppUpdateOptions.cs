namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// Self-update configuration. <see cref="FeedUrl"/> is null until an update
/// feed is published — the update page labels self-updates honestly
/// unavailable in the meantime. Do NOT invent a feed URL here.
/// </summary>
public sealed class AppUpdateOptions
{
    /// <summary>
    /// Base URL hosting <c>releases.{channel}.json</c> beside the
    /// <c>.nupkg</c> files, e.g. from <c>vpk upload</c>. Null = no feed yet.
    /// </summary>
    public string? FeedUrl { get; set; }
}
