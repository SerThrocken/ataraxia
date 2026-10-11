using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class ScheduleParserTests
{
    private static ParsedSchedule Parse(string input)
    {
        Assert.True(ScheduleParser.TryParse(input, out var schedule), $"should parse: {input}");
        return schedule!;
    }

    [Fact]
    public void MorningBriefMapsToDailySevenAm()
    {
        var s = Parse("brief me every morning at 7");
        Assert.Equal("0 7 * * *", s.CronExpression);
        Assert.Equal("brief me", s.Prompt);
        Assert.Equal("Every day at 7:00 AM", s.Summary);
        Assert.Null(s.Repeat);
    }

    [Fact]
    public void WeekdayStandupKeepsTrailingPrompt()
    {
        var s = Parse("every weekday at 9am stand up");
        Assert.Equal("0 9 * * 1-5", s.CronExpression);
        Assert.Equal("stand up", s.Prompt);
        Assert.Equal("Weekdays at 9:00 AM", s.Summary);
    }

    [Fact]
    public void WeekendWatering()
    {
        var s = Parse("every weekend at 10am water plants");
        Assert.Equal("0 10 * * 0,6", s.CronExpression);
        Assert.Equal("water plants", s.Prompt);
        Assert.Equal("Weekends at 10:00 AM", s.Summary);
    }

    [Fact]
    public void SpecificDay()
    {
        var s = Parse("standup every monday at 9");
        Assert.Equal("0 9 * * 1", s.CronExpression);
        Assert.Equal("standup", s.Prompt);
        Assert.Equal("Mondays at 9:00 AM", s.Summary);
    }

    [Fact]
    public void EveningBareHourIsPm()
    {
        var s = Parse("every evening at 6 review the day");
        Assert.Equal("0 18 * * *", s.CronExpression);
        Assert.Equal("review the day", s.Prompt);
    }

    [Fact]
    public void EveryThirtyMinutes()
    {
        var s = Parse("every 30 minutes check server");
        Assert.Equal("*/30 * * * *", s.CronExpression);
        Assert.Equal("check server", s.Prompt);
        Assert.Equal("Every 30 minutes", s.Summary);
    }

    [Fact]
    public void EveryTwoHours()
    {
        var s = Parse("remind me to stretch every 2 hours");
        Assert.Equal("0 */2 * * *", s.CronExpression);
        Assert.Equal("remind me to stretch", s.Prompt);
        Assert.Equal("Every 2 hours", s.Summary);
    }

    [Fact]
    public void HourlyKeyword()
    {
        var s = Parse("hourly backup reminder");
        Assert.Equal("0 * * * *", s.CronExpression);
        Assert.Equal("backup reminder", s.Prompt);
    }

    [Fact]
    public void PmTime()
    {
        var s = Parse("remind me every day at 6:30pm to check the printers");
        Assert.Equal("30 18 * * *", s.CronExpression);
        Assert.Equal("remind me to check the printers", s.Prompt);
        Assert.Equal("Every day at 6:30 PM", s.Summary);
    }

    [Fact]
    public void OnceSetsRepeat()
    {
        var s = Parse("remind me once at 3pm to call mom");
        Assert.Equal("0 15 * * *", s.CronExpression);
        Assert.Equal("once", s.Repeat);
        var req = ScheduleParser.ToCreateRequest(s);
        Assert.Equal(1, req.Repeat);
    }

    [Fact]
    public void NoonKeyword()
    {
        var s = Parse("noon briefing every day");
        Assert.Equal("0 12 * * *", s.CronExpression);
        // "noon" is a schedule word, so it is stripped from the prompt.
        Assert.Equal("briefing", s.Prompt);
    }

    [Fact]
    public void TwentyFourHourTime()
    {
        var s = Parse("every day at 19:00 sync notes");
        Assert.Equal("0 19 * * *", s.CronExpression);
        Assert.Equal("sync notes", s.Prompt);
    }

    [Fact]
    public void PlainChatIsNotASchedule()
    {
        Assert.False(ScheduleParser.TryParse("hello world", out _));
        Assert.False(ScheduleParser.TryParse("remind me to call mom", out _));
        Assert.False(ScheduleParser.TryParse("", out _));
        Assert.False(ScheduleParser.TryParse(null, out _));
    }

    [Fact]
    public void ToCreateRequestBuildsJobsBody()
    {
        var s = Parse("brief me every morning at 7");
        var req = ScheduleParser.ToCreateRequest(s, paused: true);
        Assert.Equal("brief me", req.Prompt);
        Assert.Equal("0 7 * * *", req.Schedule);
        Assert.Equal("local", req.Deliver);
        Assert.True(req.Paused);
        Assert.Equal("brief me", req.Name);
    }
}
