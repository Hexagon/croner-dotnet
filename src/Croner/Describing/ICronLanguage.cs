namespace Croner.Describing;

/// <summary>
/// Supplies the localized phrases used by <see cref="Cron.Describe(ICronLanguage)"/>.
/// Implement it to describe patterns in another language. Members with a default
/// implementation have English defaults and are optional to override.
/// </summary>
public interface ICronLanguage
{
    /// <summary>"Every minute"</summary>
    string EveryMinute { get; }

    /// <summary>"Every second"</summary>
    string EverySecondPhrase { get; }

    /// <summary>"every 5 minutes"</summary>
    string EveryXMinutes(int step);

    /// <summary>"every 5 seconds"</summary>
    string EveryXSeconds(int step);

    /// <summary>"of every 2 hours"</summary>
    string EveryXHours(int step);

    /// <summary>"At 14:00"</summary>
    string AtTime(string time);

    /// <summary>"At 14:00, every 10 seconds"</summary>
    string AtTimeAndEveryXSeconds(string time, int step);

    /// <summary>"At 14:00, at second 10-20"</summary>
    string AtTimeAtSecond(string time, string second);

    /// <summary>"At {phrase}"</summary>
    string AtPhrase(string phrase);

    /// <summary>"on {phrase}"</summary>
    string OnPhrase(string phrase);

    /// <summary>"in {phrase}"</summary>
    string InPhrase(string phrase);

    /// <summary>"second {s}"</summary>
    string SecondPhrase(string s);

    /// <summary>"minute {s}"</summary>
    string MinutePhrase(string s);

    /// <summary>"{s} past every hour"</summary>
    string MinutePastEveryHourPhrase(string s);

    /// <summary>"of hour {s}"</summary>
    string HourPhrase(string s);

    /// <summary>"year {s}"</summary>
    string YearPhrase(string s);

    /// <summary>"day {s}"</summary>
    string DayPhrase(string s);

    /// <summary>"the last day of the month"</summary>
    string TheLastDayOfTheMonth { get; }

    /// <summary>"the weekday nearest day {day}"</summary>
    string TheWeekdayNearestDay(string day);

    /// <summary>"the last {day} of the month"</summary>
    string TheLastWeekdayOfTheMonth(string day);

    /// <summary>"the 2nd {day} of the month"</summary>
    string TheNthWeekdayOfTheMonth(int n, string day);

    /// <summary>"(if it is also {dow})"</summary>
    string DomAndDowIfAlso(string dow);

    /// <summary>"(if it is also one of: {dow})"</summary>
    string DomAndDowIfAlsoOneOf(string dow);

    /// <summary>"and"</summary>
    string ListConjunctionAnd { get; }

    /// <summary>"or"</summary>
    string ListConjunctionOr { get; }

    /// <summary>Day names, Sunday first.</summary>
    string[] DayOfWeekNames { get; }

    /// <summary>Month names, January first.</summary>
    string[] MonthNames { get; }

    /// <summary>Formats a time of day, e.g. "14:00" or "14:00:30". Override for a 12-hour clock.</summary>
    string TimeOfDay(int hour, int minute, int? second) =>
        second is int s ? $"{hour:00}:{minute:00}:{s:00}" : $"{hour:00}:{minute:00}";

    /// <summary>Joins a repeating phrase with an hour description, e.g. "Every minute past hour 0".</summary>
    string EveryPastPhrase(string every, string hours) => $"{every} past {hours}";

    /// <summary>The hour part of <see cref="EveryPastPhrase"/>, e.g. "hour 0 and 12".</summary>
    string PastHourPhrase(string s) => $"hour {s}";

    /// <summary>"every 2 months"</summary>
    string EveryXMonths(int step) => $"every {step} months";

    /// <summary>"year every 2"</summary>
    string EveryXYears(int step) => YearPhrase($"every {step}");
}
