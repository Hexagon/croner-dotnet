using System.Collections.Generic;

namespace Croner.Describing;

/// <summary>Swedish phrases for pattern descriptions.</summary>
public sealed class Swedish : ICronLanguage
{
    /// <summary>The shared instance.</summary>
    public static Swedish Instance { get; } = new();

    /// <inheritdoc />
    public string EveryMinute => "Varje minut";
    /// <inheritdoc />
    public string EverySecond => "Varje sekund";
    /// <inheritdoc />
    public string EveryXMinutes(int interval) => $"var {interval}:e minut";
    /// <inheritdoc />
    public string EveryXSeconds(int interval) => $"var {interval}:e sekund";
    /// <inheritdoc />
    public string EveryXHours(int interval) => $"var {interval}:e timme";
    /// <inheritdoc />
    public string AtTime(string time) => $"Klockan {time}";
    /// <inheritdoc />
    public string AtTimeAndEveryXSeconds(string time, int interval) => $"Klockan {time}, var {interval}:e sekund";
    /// <inheritdoc />
    public string AtTimeAtSecond(string time, string second) => $"Klockan {time}, på sekund {second}";
    /// <inheritdoc />
    public string AtPhrase(string phrase) => $"Vid {phrase}";
    /// <inheritdoc />
    public string OnPhrase(string phrase) => $"på {phrase}";
    /// <inheritdoc />
    public string InPhrase(string phrase) => $"i {phrase}";
    /// <inheritdoc />
    public string SecondPhrase(string s) => $"sekund {s}";
    /// <inheritdoc />
    public string MinutePhrase(string s) => $"minut {s}";
    /// <inheritdoc />
    public string MinutePastEveryHourPhrase(string s) => $"{s} över varje heltimme";
    /// <inheritdoc />
    public string HourPhrase(string s) => $"timme {s}";
    /// <inheritdoc />
    public string YearPhrase(string s) => $"år {s}";
    /// <inheritdoc />
    public string DayPhrase(string s) => $"dag {s}";
    /// <inheritdoc />
    public string TheLastDayOfTheMonth => "sista dagen i månaden";
    /// <inheritdoc />
    public string TheWeekdayNearestDay(string day) => $"veckodagen närmast dag {day}";
    /// <inheritdoc />
    public string TheLastWeekdayOfTheMonth(string day) => $"sista {day} i månaden";

    /// <inheritdoc />
    public string TheNthWeekdayOfTheMonth(int n, string day)
    {
        string ordinal = n switch
        {
            1 => "första",
            2 => "andra",
            3 => "tredje",
            4 => "fjärde",
            5 => "femte",
            _ => "",
        };
        return $"den {ordinal} {day} i månaden";
    }

    /// <inheritdoc />
    public string IfAlsoDayOfWeek(string dow) => $"(om det också är {dow})";
    /// <inheritdoc />
    public string IfAlsoOneOfDaysOfWeek(string dow) => $"(om det också är en av: {dow})";
    /// <inheritdoc />
    public string ListConjunctionAnd => "och";
    /// <inheritdoc />
    public string ListConjunctionOr => "eller";

    /// <inheritdoc />
    public IReadOnlyList<string> DayOfWeekNames { get; } =
        ["söndag", "måndag", "tisdag", "onsdag", "torsdag", "fredag", "lördag"];

    /// <inheritdoc />
    public IReadOnlyList<string> MonthNames { get; } =
    [
        "januari", "februari", "mars", "april", "maj", "juni",
        "juli", "augusti", "september", "oktober", "november", "december",
    ];

    /// <inheritdoc />
    public string EveryPastPhrase(string every, string hours) => $"{every} under {hours}";
    /// <inheritdoc />
    public string PastHourPhrase(string s) => $"timme {s}";
    /// <inheritdoc />
    public string EveryXMonths(int interval) => $"var {interval}:e månad";
    /// <inheritdoc />
    public string EveryXYears(int interval) => $"var {interval}:e år";
}
