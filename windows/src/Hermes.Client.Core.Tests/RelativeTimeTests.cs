using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class RelativeTimeTests
{
    [Fact]
    public void NullRendersEmpty() => Assert.Equal("", RelativeTime.Format(null));

    [Fact]
    public void UnderAMinuteIsJustNow() =>
        Assert.Equal("just now", RelativeTime.Format(DateTimeOffset.UtcNow.AddSeconds(-30)));

    [Fact]
    public void MinutesAgo() =>
        Assert.Equal("5m ago", RelativeTime.Format(DateTimeOffset.UtcNow.AddMinutes(-5)));

    [Fact]
    public void HoursAgo() =>
        Assert.Equal("3h ago", RelativeTime.Format(DateTimeOffset.UtcNow.AddHours(-3)));

    [Fact]
    public void DaysAgo() =>
        Assert.Equal("2d ago", RelativeTime.Format(DateTimeOffset.UtcNow.AddDays(-2)));

    [Fact]
    public void OlderThanAWeekRendersDate()
    {
        var ts = DateTimeOffset.UtcNow.AddDays(-40);
        Assert.Equal(ts.ToLocalTime().ToString("MMM d"), RelativeTime.Format(ts));
    }
}
