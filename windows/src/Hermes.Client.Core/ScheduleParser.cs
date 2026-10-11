using System.Text.RegularExpressions;

namespace Hermes.Client.Core;

/// <summary>
/// Rule-based plain-language schedule parser (v1, no LLM). Turns utterances
/// like "brief me every morning at 7" or "every weekday at 9am stand up"
/// into a cron expression plus the agent prompt, ready for POST /api/jobs.
/// Unrecognized input returns false — the dialog then asks for explicit fields.
/// </summary>
public sealed record ParsedSchedule(
    string Prompt,
    string CronExpression,
    string? Name,
    string Summary,
    string? Repeat);

public static partial class ScheduleParser
{
    // Time: "7", "7am", "7:30", "7:30pm", "19:00", "noon", "midnight".
    [GeneratedRegex(@"(?<h>\d{1,2})(:(?<m>\d{2}))?\s*(?<ap>[ap]\.?m\.?)?", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"\b(?<n>\d{1,3})\s*(minutes?|mins?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EveryMinutesRegex();

    [GeneratedRegex(@"\b(?<n>\d{1,2})\s*(hours?|hrs?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EveryHoursRegex();

    [GeneratedRegex(@"\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayNameRegex();

    private static readonly Dictionary<string, int> DayNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sunday"] = 0, ["monday"] = 1, ["tuesday"] = 2, ["wednesday"] = 3,
        ["thursday"] = 4, ["friday"] = 5, ["saturday"] = 6,
    };

    private static readonly (string Word, int Hour, int Minute)[] NamedTimes =
    [
        ("midnight", 0, 0), ("noon", 12, 0), ("morning", 8, 0),
        ("afternoon", 14, 0), ("evening", 18, 0), ("night", 21, 0),
    ];

    public static bool TryParse(string? input, out ParsedSchedule? schedule)
    {
        schedule = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        var lower = text.ToLowerInvariant();
        var repeat = Regex.IsMatch(lower, @"\b(just\s+)?once\b") ? "once" : null;

        // Interval forms first: "every 30 minutes", "every 2 hours", "hourly".
        if (TryParseInterval(lower) is { } interval)
        {
            schedule = Finish(text, interval.Cron, interval.Summary, repeat);
            return schedule is not null;
        }

        var (hour, minute, timeSpan) = ExtractTime(lower);
        var (dowCron, dowSummary) = ExtractDayOfWeek(lower);

        // A schedule clause must name a day pattern or a time; otherwise this
        // is not a scheduling utterance at all.
        var hasDay = dowCron is not null;
        var hasTime = timeSpan is not null;
        var hasNamedTime = NamedTimes.Any(t => Regex.IsMatch(lower, $@"\b{t.Word}\b"));
        if (!hasDay && !hasTime && !hasNamedTime && !lower.Contains("daily") && !lower.Contains("every"))
        {
            return false;
        }

        var cron = $"{minute} {hour} * * {dowCron ?? "*"}";
        var summary = dowSummary is null
            ? $"Every day at {FormatTime(hour, minute)}"
            : $"{dowSummary} at {FormatTime(hour, minute)}";
        schedule = Finish(text, cron, summary, repeat);
        return schedule is not null;
    }

    private static ParsedSchedule? Finish(
        string text, string cron, string summary, string? repeat)
    {
        var prompt = ExtractPrompt(text);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var name = string.Join(' ', prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6));
        return new ParsedSchedule(prompt, cron, name, summary, repeat);
    }

    private static (string Cron, string Summary)? TryParseInterval(string lower)
    {
        if (Regex.IsMatch(lower, @"\bhourly\b"))
        {
            return ("0 * * * *", "Every hour");
        }

        var minutes = EveryMinutesRegex().Match(lower);
        if (minutes.Success && int.TryParse(minutes.Groups["n"].Value, out var m) && m is >= 1 and <= 59)
        {
            return ($"*/{m} * * * *", m == 1 ? "Every minute" : $"Every {m} minutes");
        }

        var hours = EveryHoursRegex().Match(lower);
        if (hours.Success && int.TryParse(hours.Groups["n"].Value, out var h) && h is >= 1 and <= 23)
        {
            return ($"0 */{h} * * *", h == 1 ? "Every hour" : $"Every {h} hours");
        }

        return null;
    }

    private static (int Hour, int Minute, Match? TimeMatch) ExtractTime(string lower)
    {
        // Prefer a time pinned to "at"; fall back to any bare time.
        var atMatch = Regex.Match(lower, @"\bat\s+(?<t>.{1,12})");
        var candidate = atMatch.Success ? atMatch.Groups["t"].Value : lower;

        // Named times are detected against the whole utterance ("evening" in
        // "every evening at 6" sits before the "at").
        foreach (var (word, hour, minute) in NamedTimes)
        {
            if (Regex.IsMatch(lower, $@"\b{word}\b"))
            {
                // An explicit clock time next to the word wins ("morning at 7").
                var explicit_ = TimeRegex().Match(candidate);
                if (explicit_.Success && TryReadClock(explicit_, out var eh, out var em))
                {
                    // "evening at 6" means 18:00 — a bare 1-11 next to an
                    // afternoon/evening/night word is PM unless marked am.
                    var ap = explicit_.Groups["ap"].Value;
                    if (string.IsNullOrEmpty(ap) && eh < 12
                        && (word is "afternoon" or "evening" or "night"))
                    {
                        eh += 12;
                    }

                    return (eh, em, explicit_);
                }

                return (hour, minute, null);
            }
        }

        var time = TimeRegex().Match(candidate);
        if (time.Success && TryReadClock(time, out var ch, out var cm))
        {
            return (ch, cm, time);
        }

        // Default: 9:00 AM when the utterance schedules without a time.
        return (9, 0, null);
    }

    private static bool TryReadClock(Match match, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        if (!int.TryParse(match.Groups["h"].Value, out var h))
        {
            return false;
        }

        var m = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value) : 0;
        var ap = match.Groups["ap"].Value.Replace(".", "").ToLowerInvariant();
        if (ap == "pm" && h < 12)
        {
            h += 12;
        }
        else if (ap == "am" && h == 12)
        {
            h = 0;
        }

        if (h is < 0 or > 23 || m is < 0 or > 59)
        {
            return false;
        }

        hour = h;
        minute = m;
        return true;
    }

    private static (string? Cron, string? Summary) ExtractDayOfWeek(string lower)
    {
        if (Regex.IsMatch(lower, @"\bweekdays?\b"))
        {
            return ("1-5", "Weekdays");
        }

        if (Regex.IsMatch(lower, @"\bweekends?\b"))
        {
            return ("0,6", "Weekends");
        }

        if (Regex.IsMatch(lower, @"\bdaily\b") || Regex.IsMatch(lower, @"\bevery\s+day\b"))
        {
            return ("*", "Every day");
        }

        var day = WeekdayNameRegex().Match(lower);
        if (day.Success && DayNumbers.TryGetValue(day.Value, out var num))
        {
            var name = char.ToUpperInvariant(day.Value[0]) + day.Value[1..];
            return (num.ToString(), $"{name}s");
        }

        return (null, null);
    }

    private static string FormatTime(int hour, int minute)
    {
        var suffix = hour < 12 ? "AM" : "PM";
        var h12 = hour % 12;
        if (h12 == 0)
        {
            h12 = 12;
        }

        return $"{h12}:{minute:00} {suffix}";
    }

    private const string TimeTail = @"\d{1,2}(:\d{2})?\s*([ap]\.?m\.?)?";

    /// <summary>
    /// The prompt is the utterance minus its schedule clause: "brief me every
    /// morning at 7" keeps "brief me"; "every weekday at 9am stand up" keeps
    /// "stand up". Leftover filler words are trimmed.
    /// </summary>
    private static string ExtractPrompt(string text)
    {
        // Schedule phrases, longest first so "every weekday at 9am" wins over
        // a bare "at 9am". The prompt is whatever remains after removing them.
        var phrases = new[]
        {
            $@"\bevery\s+\d{{1,3}}\s*(minutes?|mins?|hours?|hrs?)\b",
            $@"\bevery\s+(weekdays?|weekends?|day|morning|evening|night|afternoon)\b(\s+at\s+{TimeTail}|\s+{TimeTail})?",
            $@"\bevery\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b(\s+at\s+{TimeTail}|\s+{TimeTail})?",
            $@"\b(daily|nightly)\b(\s+at\s+{TimeTail})?",
            @"\bhourly\b",
            $@"\bat\s+{TimeTail}\b",
            @"\b(midnight|noon|morning|afternoon|evening|night)\b",
        };

        var prompt = text;
        foreach (var phrase in phrases)
        {
            prompt = Regex.Replace(prompt, phrase, " ", RegexOptions.IgnoreCase);
        }

        prompt = Regex.Replace(prompt, @"\s+", " ").Trim().Trim(',', ';', ':');
        prompt = Regex.Replace(prompt, @"^(please\s+|just\s+)", "", RegexOptions.IgnoreCase).Trim();
        return prompt.Trim().TrimEnd('.', '!', '?');
    }

    /// <summary>Builds the POST /api/jobs body from a parsed schedule.</summary>
    public static CronJobCreateRequest ToCreateRequest(ParsedSchedule schedule, bool paused = false) =>
        new(
            Name: schedule.Name ?? "Scheduled prompt",
            Prompt: schedule.Prompt,
            Schedule: schedule.CronExpression,
            Deliver: "local",
            Paused: paused,
            // The gateway also accepts repeat:'once'; the client DTO models
            // repeat as int?, so "once" maps to a single run.
            Repeat: schedule.Repeat == "once" ? 1 : null);
}
