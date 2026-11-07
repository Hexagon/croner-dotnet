using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Croner.Describing;

/// <summary>Builds human-readable descriptions of cron patterns.</summary>
public static class Describer
{
    /// <summary>Describes <paramref name="pattern"/> using <paramref name="lang"/>.</summary>
    public static string Describe(CronPattern pattern, ICronLanguage lang)
    {
        var parts = new List<string>();
        foreach (string part in new[]
                 {
                     DescribeTime(pattern, lang),
                     DescribeDay(pattern, lang),
                     DescribeMonth(pattern, lang),
                     DescribeYear(pattern, lang),
                 })
        {
            if (part.Length > 0)
            {
                parts.Add(part);
            }
        }

        string description = string.Join(", ", parts);
        if (description.Length > 0)
        {
            description = char.ToUpper(description[0], CultureInfo.InvariantCulture) + description.Substring(1) + ".";
        }
        return description;
    }

    // A component is "all set" if every possible value is included.
    private static bool IsAllSet(CronComponent c)
    {
        if (c.Step != 1)
        {
            return false;
        }
        int total = c.Max - c.Min + 1;
        if (total > 10000)
        {
            return c.FromWildcard;
        }
        return c.CountSetValues(CronBits.All) == total;
    }

    private static bool IsSteppedFromStart(int step, List<int> vals, int min) =>
        step > 1 && vals.Count > 0 && vals[0] == min;

    private static string DescribeTime(CronPattern p, ICronLanguage lang)
    {
        List<int> sec = p.Seconds.GetSetValues(CronBits.All);
        List<int> min = p.Minutes.GetSetValues(CronBits.All);
        List<int> hour = p.Hours.GetSetValues(CronBits.All);

        bool isDefaultSeconds = p.Seconds.Step == 1 && sec.Count == 1 && sec[0] == 0;
        bool isEverySecond = IsAllSet(p.Seconds);

        if (isEverySecond && IsAllSet(p.Minutes) && IsAllSet(p.Hours))
        {
            return lang.EverySecondPhrase;
        }
        if (isDefaultSeconds && IsAllSet(p.Minutes) && IsAllSet(p.Hours))
        {
            return lang.EveryMinute;
        }
        if (isDefaultSeconds && IsSteppedFromStart(p.Minutes.Step, min, p.Minutes.Min) && IsAllSet(p.Hours))
        {
            return lang.AtPhrase(lang.EveryXMinutes(p.Minutes.Step));
        }

        // Specific HH:MM time
        if (!isEverySecond && p.Hours.Step == 1 && hour.Count == 1 && p.Minutes.Step == 1 && min.Count == 1)
        {
            string time = lang.TimeOfDay(hour[0], min[0], null);
            if (!isDefaultSeconds)
            {
                if (IsSteppedFromStart(p.Seconds.Step, sec, p.Seconds.Min))
                {
                    return lang.AtTimeAndEveryXSeconds(time, p.Seconds.Step);
                }
                if (sec.Count == 1)
                {
                    return lang.AtTime(lang.TimeOfDay(hour[0], min[0], sec[0]));
                }
                return lang.AtTimeAtSecond(time, FormatNumberList(sec, lang));
            }
            return lang.AtTime(time);
        }

        // "* 0 * * *" -> "Every minute past hour 0"
        if (isDefaultSeconds && IsAllSet(p.Minutes) && !IsAllSet(p.Hours))
        {
            return lang.EveryPastPhrase(lang.EveryMinute, HourDescription(p, hour, lang));
        }

        // "* * 0 * * *" -> "Every second past hour 0"
        if (isEverySecond && IsAllSet(p.Minutes) && !IsAllSet(p.Hours))
        {
            return lang.EveryPastPhrase(lang.EverySecondPhrase, HourDescription(p, hour, lang));
        }

        var parts = new List<string>();
        if (isEverySecond)
        {
            parts.Add(lang.EverySecondPhrase);
        }
        else if (!isDefaultSeconds)
        {
            parts.Add(IsSteppedFromStart(p.Seconds.Step, sec, p.Seconds.Min)
                ? lang.EveryXSeconds(p.Seconds.Step)
                : lang.SecondPhrase(FormatNumberList(sec, lang)));
        }

        if (IsSteppedFromStart(p.Minutes.Step, min, p.Minutes.Min))
        {
            parts.Add(lang.EveryXMinutes(p.Minutes.Step));
        }
        else if (!IsAllSet(p.Minutes))
        {
            string minDesc = lang.MinutePhrase(FormatNumberList(min, lang));
            parts.Add(IsAllSet(p.Hours) && p.Hours.Step == 1 ? lang.MinutePastEveryHourPhrase(minDesc) : minDesc);
        }

        if (!IsAllSet(p.Hours))
        {
            parts.Add(IsSteppedFromStart(p.Hours.Step, hour, p.Hours.Min)
                ? lang.EveryXHours(p.Hours.Step)
                : lang.HourPhrase(FormatNumberList(hour, lang)));
        }

        if (parts.Count == 0)
        {
            return lang.EveryMinute;
        }
        if (parts.Count > 1 && parts[0] == lang.EverySecondPhrase)
        {
            return string.Join(", ", parts);
        }
        return lang.AtPhrase(string.Join(", ", parts));
    }

    private static string HourDescription(CronPattern p, List<int> hour, ICronLanguage lang) =>
        IsSteppedFromStart(p.Hours.Step, hour, p.Hours.Min)
            ? lang.EveryXHours(p.Hours.Step)
            : lang.PastHourPhrase(FormatNumberList(hour, lang));

    private static string FormatTextList(List<string> items, ICronLanguage lang) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} {lang.ListConjunctionAnd} {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))}, {lang.ListConjunctionAnd} {items[items.Count - 1]}",
    };

    private static string FormatNumberList(List<int> values, ICronLanguage lang)
    {
        if (values.Count == 0)
        {
            return "";
        }
        var sorted = values.OrderBy(v => v).ToList();
        var items = new List<string>();
        int i = 0;
        while (i < sorted.Count)
        {
            int start = sorted[i];
            int j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1)
            {
                j++;
            }
            if (j > i + 1)
            {
                // Only create a range for 3 or more consecutive numbers
                items.Add(string.Create(CultureInfo.InvariantCulture, $"{start}-{sorted[j]}"));
            }
            else
            {
                for (int k = i; k <= j; k++)
                {
                    items.Add(sorted[k].ToString(CultureInfo.InvariantCulture));
                }
            }
            i = j + 1;
        }
        return FormatTextList(items, lang);
    }

    private static string DescribeDay(CronPattern p, ICronLanguage lang)
    {
        if (p.IsDomWildcard && p.IsDowWildcard)
        {
            return "";
        }

        string domDesc = DescribeDom(p, lang);
        if (!p.IsDomWildcard && p.IsDowWildcard)
        {
            return lang.OnPhrase(domDesc);
        }

        List<string> dowParts = DescribeDowParts(p, lang);
        string dowDesc = FormatTextList(dowParts, lang);
        if (p.IsDomWildcard)
        {
            return lang.OnPhrase(dowDesc);
        }

        if (p.MatchesDomAndDow)
        {
            string final = dowParts.Count > 1 ? lang.DomAndDowIfAlsoOneOf(dowDesc) : lang.DomAndDowIfAlso(dowDesc);
            return $"{lang.OnPhrase(domDesc)} {final}";
        }
        return $"{lang.OnPhrase(domDesc)} {lang.ListConjunctionOr} {dowDesc}";
    }

    private static string DescribeDom(CronPattern p, ICronLanguage lang)
    {
        var parts = new List<string>();
        List<int> regular = p.Days.GetSetValues(CronBits.All);
        if (regular.Count > 0)
        {
            parts.Add(lang.DayPhrase(FormatNumberList(regular, lang)));
        }
        if (p.Days.IsFeatureEnabled(CronBits.Last))
        {
            parts.Add(lang.TheLastDayOfTheMonth);
        }
        List<int> weekdays = p.Days.GetSetValues(CronBits.ClosestWeekday);
        if (weekdays.Count > 0)
        {
            parts.Add(lang.TheWeekdayNearestDay(FormatNumberList(weekdays, lang)));
        }
        return FormatTextList(parts, lang);
    }

    private static List<string> DescribeDowParts(CronPattern p, ICronLanguage lang)
    {
        var parts = new List<string>();
        string[] names = lang.DayOfWeekNames;
        string Name(int v) => names[v % 7];

        List<int> last = p.DaysOfWeek.GetSetValues(CronBits.Last);
        if (last.Count > 0)
        {
            parts.Add(lang.TheLastWeekdayOfTheMonth(FormatTextList(last.Select(Name).ToList(), lang)));
        }

        for (int i = 0; i < CronBits.NthBits.Length; i++)
        {
            List<int> values = p.DaysOfWeek.GetSetValues(CronBits.NthBits[i]);
            if (values.Count > 0)
            {
                parts.Add(lang.TheNthWeekdayOfTheMonth(i + 1, FormatTextList(values.Select(Name).ToList(), lang)));
            }
        }

        List<int> regular = p.DaysOfWeek.GetSetValues(CronBits.All);
        if (regular.Count > 0)
        {
            parts.Add(FormatTextList(regular.Select(Name).ToList(), lang));
        }
        return parts;
    }

    private static string DescribeMonth(CronPattern p, ICronLanguage lang)
    {
        if (IsAllSet(p.Months))
        {
            return "";
        }
        if (p.Months.Step > 1)
        {
            return lang.InPhrase(lang.EveryXMonths(p.Months.Step));
        }
        List<string> list = p.Months.GetSetValues(CronBits.All).Select(v => lang.MonthNames[v - 1]).ToList();
        return lang.InPhrase(FormatTextList(list, lang));
    }

    private static string DescribeYear(CronPattern p, ICronLanguage lang)
    {
        if (IsAllSet(p.Years))
        {
            return "";
        }
        if (p.Years.Step > 1)
        {
            return lang.InPhrase(lang.EveryXYears(p.Years.Step));
        }
        return lang.InPhrase(lang.YearPhrase(FormatNumberList(p.Years.GetSetValues(CronBits.All), lang)));
    }
}
