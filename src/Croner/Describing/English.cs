using System.Collections.Generic;

namespace Croner.Describing;

/// <summary>English phrases for pattern descriptions (the default).</summary>
public sealed class English : ICronLanguage
{
    /// <summary>The shared instance.</summary>
    public static English Instance { get; } = new();

    /// <inheritdoc />
    public string EveryMinute => "Every minute";
    /// <inheritdoc />
    public string EverySecond => "Every second";
    /// <inheritdoc />
    public string EveryXMinutes(int interval) => $"every {interval} minutes";
    /// <inheritdoc />
    public string EveryXSeconds(int interval) => $"every {interval} seconds";
    /// <inheritdoc />
    public string EveryXHours(int interval) => $"of every {interval} hours";
    /// <inheritdoc />
    public string AtTime(string time) => $"At {time}";
    /// <inheritdoc />
    public string AtTimeAndEveryXSeconds(string time, int interval) => $"At {time}, every {interval} seconds";
    /// <inheritdoc />
    public string AtTimeAtSecond(string time, string second) => $"At {time}, at second {second}";
    /// <inheritdoc />
    public string AtPhrase(string phrase) => $"At {phrase}";
    /// <inheritdoc />
    public string OnPhrase(string phrase) => $"on {phrase}";
    /// <inheritdoc />
    public string InPhrase(string phrase) => $"in {phrase}";
    /// <inheritdoc />
    public string SecondPhrase(string s) => $"second {s}";
    /// <inheritdoc />
    public string MinutePhrase(string s) => $"minute {s}";
    /// <inheritdoc />
    public string MinutePastEveryHourPhrase(string s) => $"{s} past every hour";
    /// <inheritdoc />
    public string HourPhrase(string s) => $"of hour {s}";
    /// <inheritdoc />
    public string YearPhrase(string s) => $"year {s}";
    /// <inheritdoc />
    public string DayPhrase(string s) => $"day {s}";
    /// <inheritdoc />
    public string TheLastDayOfTheMonth => "the last day of the month";
    /// <inheritdoc />
    public string TheWeekdayNearestDay(string day) => $"the weekday nearest day {day}";
    /// <inheritdoc />
    public string TheLastWeekdayOfTheMonth(string day) => $"the last {day} of the month";

    /// <inheritdoc />
    public string TheNthWeekdayOfTheMonth(int n, string day)
    {
        string suffix = n switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"the {n}{suffix} {day} of the month";
    }

    /// <inheritdoc />
    public string IfAlsoDayOfWeek(string dow) => $"(if it is also {dow})";
    /// <inheritdoc />
    public string IfAlsoOneOfDaysOfWeek(string dow) => $"(if it is also one of: {dow})";
    /// <inheritdoc />
    public string ListConjunctionAnd => "and";
    /// <inheritdoc />
    public string ListConjunctionOr => "or";

    /// <inheritdoc />
    public IReadOnlyList<string> DayOfWeekNames { get; } =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    /// <inheritdoc />
    public IReadOnlyList<string> MonthNames { get; } =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ];
}
