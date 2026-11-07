using System;

namespace Croner;

/// <summary>
/// A parsed cron pattern: one <see cref="CronComponent"/> per field, plus the
/// options that decide how the day-of-month and day-of-week fields combine.
/// </summary>
public sealed class CronPattern : IEquatable<CronPattern>
{
    internal CronPattern(
        string pattern,
        CronComponent seconds,
        CronComponent minutes,
        CronComponent hours,
        CronComponent days,
        CronComponent months,
        CronComponent daysOfWeek,
        CronComponent years,
        bool starDom,
        bool starDow,
        bool domAndDow)
    {
        Expression = pattern;
        Seconds = seconds;
        Minutes = minutes;
        Hours = hours;
        DaysOfMonth = days;
        Months = months;
        DaysOfWeek = daysOfWeek;
        Years = years;
        IsDayOfMonthWildcard = starDom;
        IsDayOfWeekWildcard = starDow;
        RequiresBothDayFields = domAndDow;
    }

    /// <summary>The pattern string, after nicknames such as <c>@daily</c> were expanded and it was upper-cased.</summary>
    public string Expression { get; }

    /// <summary>The seconds field (0-59).</summary>
    public CronComponent Seconds { get; }

    /// <summary>The minutes field (0-59).</summary>
    public CronComponent Minutes { get; }

    /// <summary>The hours field (0-23).</summary>
    public CronComponent Hours { get; }

    /// <summary>The day-of-month field (1-31).</summary>
    public CronComponent DaysOfMonth { get; }

    /// <summary>The month field (1-12).</summary>
    public CronComponent Months { get; }

    /// <summary>The day-of-week field (0-6, 0 is Sunday).</summary>
    public CronComponent DaysOfWeek { get; }

    /// <summary>The year field.</summary>
    public CronComponent Years { get; }

    /// <summary>
    /// Whether the day-of-month field is <c>*</c>. Unless <see cref="RequiresBothDayFields"/> is set,
    /// a day matches when either its day of month or its weekday matches, except that a <c>*</c>
    /// field is ignored.
    /// </summary>
    public bool IsDayOfMonthWildcard { get; }

    /// <summary>Whether the day-of-week field is <c>*</c>. See <see cref="IsDayOfMonthWildcard"/>.</summary>
    public bool IsDayOfWeekWildcard { get; }

    /// <summary>
    /// Whether a day must match both the day-of-month and day-of-week fields, from the <c>+</c>
    /// prefix or the parser's <see cref="CronParserOptions.RequireBothDayFields"/> option.
    /// </summary>
    public bool RequiresBothDayFields { get; }

    internal bool YearMatch(int year)
    {
        if (year < Cron.MinYear || year > Cron.MaxYear)
        {
            return false;
        }
        return Years.Has(year, CronBits.All);
    }

    // Days 1-7 hold the first occurrence of every weekday, days 8-14 the second, and so on.
    private static byte NthWeekdayBit(int day)
    {
        int idx = (day - 1) / 7;
        return idx < 5 ? CronBits.NthBits[idx] : CronBits.None;
    }

    private static DayOfWeek Shift(DayOfWeek weekday, int days) =>
        (DayOfWeek)((((int)weekday + days) % 7 + 7) % 7);

    internal bool DayMatch(int year, int month, int day, DayOfWeek weekday)
    {
        if (month < 1 || month > 12)
        {
            throw new CronException(CronErrorKind.InvalidDate);
        }
        int monthLength = DateTime.DaysInMonth(year, month);
        if (day < 1 || day > monthLength)
        {
            throw new CronException(CronErrorKind.InvalidDate);
        }

        int weekdayBit = (int)weekday;
        bool dayMatches = DaysOfMonth.Has(day, CronBits.All);
        bool dowMatches = false;

        bool lastEnabled = DaysOfMonth.IsFeatureEnabled(CronBits.Last);
        bool closestEnabled = DaysOfMonth.IsFeatureEnabled(CronBits.ClosestWeekday);

        // LW (last weekday) - both LAST and CLOSEST_WEEKDAY enabled
        if (!dayMatches && lastEnabled && closestEnabled
            && day == LastWeekdayOfMonth(monthLength, Shift(weekday, monthLength - day)))
        {
            dayMatches = true;
        }
        else if (!dayMatches && lastEnabled && !closestEnabled && day == monthLength)
        {
            // L (last day of month)
            dayMatches = true;
        }

        if (!dayMatches && ClosestWeekday(monthLength, day, weekday))
        {
            dayMatches = true;
        }

        // A day can only be one occurrence of its own weekday, so only that nth bit needs testing.
        byte nthBit = NthWeekdayBit(day);
        if (nthBit != CronBits.None)
        {
            dowMatches = DaysOfWeek.Has(weekdayBit, nthBit);
        }

        // The last occurrence of a weekday has no room for another seven days in the month.
        if (!dowMatches && DaysOfWeek.Has(weekdayBit, CronBits.Last) && day + 7 > monthLength)
        {
            dowMatches = true;
        }

        dowMatches = dowMatches || DaysOfWeek.Has(weekdayBit, CronBits.All);

        if ((dayMatches && IsDayOfWeekWildcard) || (dowMatches && IsDayOfMonthWildcard))
        {
            return true;
        }
        if (!IsDayOfMonthWildcard && !IsDayOfWeekWildcard)
        {
            return RequiresBothDayFields ? dayMatches && dowMatches : dayMatches || dowMatches;
        }
        return false;
    }

    private static int LastWeekdayOfMonth(int monthLength, DayOfWeek lastWeekday) => lastWeekday switch
    {
        DayOfWeek.Saturday => monthLength - 1,
        DayOfWeek.Sunday => monthLength - 2,
        _ => monthLength,
    };

    // Checks whether a 'W' day in the pattern resolves to `day`.
    private bool ClosestWeekday(int monthLength, int day, DayOfWeek weekday)
    {
        // The 'W' rule never moves a date by more than two days.
        int first = Math.Max(day - 2, 1);
        int last = Math.Min(day + 2, monthLength);
        for (int patternDay = first; patternDay <= last; patternDay++)
        {
            if (!DaysOfMonth.Has(patternDay, CronBits.ClosestWeekday))
            {
                continue;
            }

            int targetDay = Shift(weekday, patternDay - day) switch
            {
                // A Saturday moves back to Friday, or forward to Monday when Friday is in the previous month.
                DayOfWeek.Saturday => patternDay > 1 ? patternDay - 1 : patternDay + 2,
                // A Sunday moves forward to Monday, or back to Friday when Monday is in the next month.
                DayOfWeek.Sunday => patternDay < monthLength ? patternDay + 1 : patternDay - 2,
                _ => patternDay,
            };

            if (targetDay == day)
            {
                return true;
            }
        }
        return false;
    }

    internal bool MonthMatch(int month) => Months.Has(month, CronBits.All);

    internal bool HourMatch(int hour) => Hours.Has(hour, CronBits.All);

    internal bool MinuteMatch(int minute) => Minutes.Has(minute, CronBits.All);

    internal bool SecondMatch(int second) => Seconds.Has(second, CronBits.All);

    /// <summary>
    /// Finds the next (forward) or previous (backward) value at or past <paramref name="value"/>
    /// that the component matches, or <c>-1</c> if none.
    /// </summary>
    internal static int FindMatchInComponent(CronComponent component, int value, bool forward)
    {
        if (forward)
        {
            for (int v = value; v <= component.Max; v++)
            {
                if (component.Has(v, CronBits.All))
                {
                    return v;
                }
            }
        }
        else
        {
            for (int v = value; v >= component.Min; v--)
            {
                if (component.Has(v, CronBits.All))
                {
                    return v;
                }
            }
        }
        return -1;
    }

    /// <summary>Returns a human-readable English description of the pattern.</summary>
    public string Describe() => Describe(Describing.English.Instance);

    /// <summary>Returns a human-readable description of the pattern in the given language.</summary>
    public string Describe(Describing.ICronLanguage language) => Describing.Describer.Describe(this, language);

    /// <inheritdoc />
    public override string ToString() => Expression;

    /// <summary>
    /// Two patterns are equal when their parsed fields are equal, regardless of how the pattern
    /// strings were written: names or numbers, letter case, <c>?</c> or <c>*</c>, and the parser's
    /// weekday numbering do not matter.
    /// </summary>
    public bool Equals(CronPattern? other)
    {
        if (other is null)
        {
            return false;
        }
        return Seconds.Equals(other.Seconds)
            && Minutes.Equals(other.Minutes)
            && Hours.Equals(other.Hours)
            && DaysOfMonth.Equals(other.DaysOfMonth)
            && Months.Equals(other.Months)
            && DaysOfWeek.Equals(other.DaysOfWeek)
            && Years.Equals(other.Years)
            && IsDayOfMonthWildcard == other.IsDayOfMonthWildcard
            && IsDayOfWeekWildcard == other.IsDayOfWeekWildcard
            && RequiresBothDayFields == other.RequiresBothDayFields;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CronPattern);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Seconds, Minutes, Hours, DaysOfMonth, Months, DaysOfWeek, Years,
            HashCode.Combine(IsDayOfMonthWildcard, IsDayOfWeekWildcard, RequiresBothDayFields));
}
