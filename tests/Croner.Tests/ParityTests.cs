using System;
using System.Collections.Generic;
using System.Linq;
using static Croner.Tests.H;

namespace Croner.Tests;

/// <summary>
/// Parsing rules shared with croner for JavaScript (11.0.1 and later) and croner-rust (5.0.1 and later).
/// The cases are ported from their test suites.
/// </summary>
public class ParityTests
{
    private static readonly CronParserOptions Quartz = new() { QuartzWeekdays = true };

    private static IEnumerable<string> Days(string pattern, int count, DateTimeOffset from, CronParserOptions? options = null) =>
        Cron.Parse(pattern, options).GetNextOccurrences(from, TimeZoneInfo.Utc).Take(count).Select(d => d.ToString("yyyy-MM-dd"));

    private static IEnumerable<string> PreviousDays(string pattern, int count, DateTimeOffset from) =>
        Cron.Parse(pattern).GetPreviousOccurrences(from, TimeZoneInfo.Utc).Take(count).Select(d => d.ToString("yyyy-MM-dd"));

    private static DateTimeOffset Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) =>
        new(y, mo, d, h, mi, s, TimeSpan.Zero);

    // ------------------------------------------------------------ numbers are digits only

    [Theory]
    // A number followed by something else; ? is read as *
    [InlineData("0 0 12* * *")]
    [InlineData("0 0 * 3* *")]
    [InlineData("0 0 * * 6*")]
    [InlineData("*/5* * * * *")]
    [InlineData("0 0 1-5* * *")]
    [InlineData("0 0 */2? * *")]
    [InlineData("0 0 5? * *")]
    [InlineData("0 0 0 * * * 20*")]
    // Day-of-month, L and W
    [InlineData("0 0 15WW * *")]
    [InlineData("0 0 15W5 * *")]
    [InlineData("0 0 5L * *")]
    [InlineData("0 0 L5 * *")]
    [InlineData("0 0 LL * *")]
    [InlineData("0 0 *L * *")]
    [InlineData("0 0 1-5L * *")]
    [InlineData("0 0 */5L * *")]
    [InlineData("0 0 L, * *")]
    [InlineData("0 0 ,L * *")]
    [InlineData("0 0 15,,L * *")]
    // Day-of-week, # and L
    [InlineData("0 0 * * 5#")]
    [InlineData("0 0 * * 5##2")]
    [InlineData("0 0 * * 5#2#3")]
    [InlineData("0 0 * * 5L#2")]
    [InlineData("0 0 * * 5LL")]
    [InlineData("0 0 * * FRIL#2")]
    [InlineData("0 0 * * FRI#")]
    [InlineData("0 0 * * 1-5#2#3")]
    // L and LW have to be values of their own
    [InlineData("0 0 LW-5 * *")]
    [InlineData("0 0 L-5 * *")]
    [InlineData("0 0 1,5L * *")]
    [InlineData("0 0 15,LW-5 * *")]
    [InlineData("0 0 LW5 * *")]
    [InlineData("0 0 L/2 * *")]
    [InlineData("0 0 L,,LW * *")]
    [InlineData("0 0 LW, * *")]
    [InlineData("0 0 WL * *")]
    public void MalformedNumbersAreRejected(string pattern)
    {
        Assert.Throws<CronException>(() => Cron.Parse(pattern));
        Assert.False(Cron.TryParse(pattern, out _));
    }

    [Fact]
    public void LWAndHashStillWork()
    {
        // 2026-11-01 and 2026-11-15 are Sundays, so 1W is Monday the 2nd and 15W Monday the 16th
        var from = Utc(2026, 11, 1);
        Assert.Equal(new[] { "2026-11-16", "2026-12-15" }, Days("0 0 15W * *", 2, from));
        Assert.Equal(new[] { "2026-11-16", "2026-12-15" }, Days("0 0 15w * *", 2, from));
        Assert.Equal(new[] { "2026-11-30", "2026-12-31" }, Days("0 0 L * *", 2, from));
        Assert.Equal(new[] { "2026-11-30", "2026-12-31" }, Days("0 0 l * *", 2, from));
        Assert.Equal(new[] { "2026-11-15", "2026-11-30" }, Days("0 0 15,L * *", 2, from));
        Assert.Equal(new[] { "2026-11-15", "2026-11-30" }, Days("0 0 L,15 * *", 2, from));
        Assert.Equal(new[] { "2026-11-30", "2026-12-31" }, Days("0 0 LW * *", 2, from));
        Assert.Equal(new[] { "2026-11-02", "2026-11-16" }, Days("0 0 1W,15W * *", 2, from));
        Assert.Equal(new[] { "2026-11-13" }, Days("0 0 * * 5#2", 1, from));
        Assert.Equal(new[] { "2026-11-13" }, Days("0 0 * * FRI#2", 1, from));
        Assert.Equal(new[] { "2026-11-27" }, Days("0 0 * * 5L", 1, from));
        Assert.Equal(new[] { "2026-11-27" }, Days("0 0 * * FRIL", 1, from));
        Assert.Equal(new[] { "2026-11-27" }, Days("0 0 * * 5#L", 1, from));
        Assert.Equal(new[] { "2026-11-09" }, Days("0 0 * * 1-5#2", 1, from));
        Assert.Equal(new[] { "2026-11-08", "2026-11-10" }, Days("0 0 * * */2#2", 2, from));
        Assert.Equal(new[] { "2026-11-02" }, Days("0 0 ? * MON", 1, from));
        Assert.Equal(new[] { "2026-11-15", "2026-12-01" }, Days("0 0 1,15 * ?", 2, from));
        Assert.Equal(new[] { "2027-01-01", "2028-01-01" }, Days("0 0 0 1 JAN * 2027-2028", 2, from));
    }

    // ------------------------------------------------------------ L and LW in one list

    [Fact]
    public void LWCanBeAnItemInADayOfMonthList()
    {
        // May 2025 ends on a Saturday: the last weekday is the 30th, the last day the 31st
        Assert.Equal(new[] { "2025-05-30", "2025-05-31" }, Days("0 0 L,LW * *", 2, Utc(2025, 5, 1)));
        Assert.Equal(new[] { "2025-05-30", "2025-05-31" }, Days("0 0 LW,L * *", 2, Utc(2025, 5, 1)));
        Assert.Equal(new[] { "2025-05-30", "2025-05-31" }, Days("0 0 l,lw 5 *", 2, Utc(2025, 1, 1)));
        Assert.Equal(new[] { "2025-05-31", "2025-05-30" }, PreviousDays("0 0 L,LW * *", 2, Utc(2025, 6, 1)));
        // A month that ends on a weekday has one run, as the last day is the last weekday
        Assert.Equal(new[] { "2025-06-30", "2025-07-31" }, Days("0 0 L,LW * *", 2, Utc(2025, 6, 1)));

        // January 2025: 1st (Wed), 15th (Wed), 31st (Fri). February 2025: the 1st is a Saturday, so 1W is
        // Monday the 3rd, the 15th is a Saturday, so 15W is Friday the 14th, and the 28th is a Friday.
        var expected = new[] { "2025-01-01", "2025-01-15", "2025-01-31", "2025-02-03", "2025-02-14", "2025-02-28" };
        Assert.Equal(expected, Days("0 0 1W,15W,LW * *", 6, Utc(2024, 12, 31, 12)));
        Assert.Equal(expected, Days("0 0 1w,15w,lw * *", 6, Utc(2024, 12, 31, 12)));
        Assert.Equal(Enumerable.Reverse(expected), PreviousDays("0 0 1W,15W,LW * *", 6, Utc(2025, 3, 1)));

        Assert.Equal(new[] { "2025-05-10", "2025-05-30", "2025-06-10" }, Days("0 0 10,LW * *", 3, Utc(2025, 5, 1)));
        Assert.Equal(new[] { "2025-05-15", "2025-05-30", "2025-05-31" }, Days("0 0 15W,L,LW * *", 3, Utc(2025, 5, 1)));

        var cron = Cron.Parse("0 0 L,LW * *");
        Assert.True(cron.IsMatch(Utc(2025, 5, 30)));
        Assert.True(cron.IsMatch(Utc(2025, 5, 31)));
        Assert.False(cron.IsMatch(Utc(2025, 5, 29)));
    }

    // ------------------------------------------------------------ names stand alone

    [Theory]
    // A day name next to a digit or another name
    [InlineData("0 12 * * SUN6")]
    [InlineData("0 12 * * 0SUN")]
    [InlineData("0 12 * * 6SUN")]
    [InlineData("0 0 * * 0MON-FRI")]
    [InlineData("0 0 * * MON-0FRI")]
    [InlineData("0 0 * * MON-FRI3")]
    [InlineData("0 0 * * SUNMON")]
    [InlineData("0 0 * * MONSUN")]
    [InlineData("0 0 * * SUNSUN")]
    [InlineData("0 0 * * SUN,MON6")]
    // A day name as a step or as the occurrence
    [InlineData("0 0 * * */FRI")]
    [InlineData("0 0 * * MON/FRI")]
    [InlineData("0 0 * * FRI#MON")]
    [InlineData("0 0 * * 5#FRI")]
    // A month name next to a digit or another name
    [InlineData("0 0 1 JAN2,JUL *")]
    [InlineData("0 0 1 0JAN,JUL *")]
    [InlineData("0 0 1 JAN,0JUL *")]
    [InlineData("0 0 1 JAN-1FEB *")]
    [InlineData("0 0 1 JAN-FEB3 *")]
    [InlineData("0 0 1 JAN1 *")]
    [InlineData("0 0 1 JANFEB *")]
    [InlineData("0 0 1 JANJAN *")]
    [InlineData("0 0 1 DEC1 *")]
    // A month name as a step
    [InlineData("0 0 1 */JAN *")]
    [InlineData("0 0 1 JAN/JUL *")]
    // Only an L can follow a day name
    [InlineData("0 0 * * FRILFRI")]
    [InlineData("0 0 * * LFRI")]
    public void NamesHaveToStandAlone(string pattern)
    {
        foreach (bool quartz in new[] { false, true })
        {
            var e = Assert.Throws<CronException>(() => Cron.Parse(pattern, new CronParserOptions { QuartzWeekdays = quartz }));
            Assert.Equal(CronErrorKind.IllegalCharacter, e.Kind);
            Assert.Contains("has to stand alone", e.Message);
        }
    }

    [Theory]
    [InlineData("0 0 * * SUN", "0 0 * * 0")]
    [InlineData("0 0 * * sun", "0 0 * * 0")]
    [InlineData("0 0 * * Mon", "0 0 * * 1")]
    [InlineData("0 0 * * MON-FRI", "0 0 * * 1-5")]
    [InlineData("0 0 * * mon-fri/2", "0 0 * * 1-5/2")]
    [InlineData("0 0 * * MON-SUN", "0 0 * * 1-7")]
    [InlineData("0 0 * * SAT-SUN", "0 0 * * 6-7")]
    [InlineData("0 0 * * SUN-SUN", "0 0 * * 0")]
    [InlineData("0 0 * * sun-sun,WED", "0 0 * * 0,3")]
    [InlineData("0 0 * * SUN-SUN#2", "0 0 * * 0#2")]
    [InlineData("0 0 * * SUN-SUN/2", "0 0 * * 0-0/2")]
    [InlineData("0 0 * * SUN-TUE,FRI", "0 0 * * 0-2,5")]
    [InlineData("0 0 * * MON,WED,FRI", "0 0 * * 1,3,5")]
    [InlineData("0 0 * * FRI#2", "0 0 * * 5#2")]
    [InlineData("0 0 * * FRIL", "0 0 * * 5L")]
    [InlineData("0 0 * * fril", "0 0 * * 5L")]
    [InlineData("0 0 * * SUNL", "0 0 * * 0L")]
    [InlineData("0 0 * * FRI#L", "0 0 * * 5#L")]
    [InlineData("0 0 * * MON-FRIL", "0 0 * * 1-5L")]
    [InlineData("0 0 * * MON-FRI#2", "0 0 * * 1-5#2")]
    [InlineData("0 0 1-15 * +MON-FRI", "0 0 1-15 * +1-5")]
    [InlineData("0 0 1 JAN *", "0 0 1 1 *")]
    [InlineData("0 0 1 dec *", "0 0 1 12 *")]
    [InlineData("0 0 1 JAN-MAR,DEC *", "0 0 1 1-3,12 *")]
    [InlineData("0 0 1 jan-jun/2 *", "0 0 1 1-6/2 *")]
    [InlineData("0 0 1 FEB,OCT-DEC *", "0 0 1 2,10-12 *")]
    [InlineData("0 0 1 OCT,NOV *", "0 0 1 10,11 *")]
    public void ANameAndItsNumberAreTheSamePattern(string named, string numbers)
    {
        var from = Utc(2026, 11, 1);
        Assert.Equal(Days(numbers, 8, from), Days(named, 8, from));
        Assert.Equal(Cron.Parse(numbers).Pattern.DaysOfWeek, Cron.Parse(named).Pattern.DaysOfWeek);
    }

    [Fact]
    public void SunSunIsSundayOnly()
    {
        // 2026-11-23 is a Monday
        Assert.Equal(new[] { "2026-11-29", "2026-12-06", "2026-12-13" }, Days("0 0 * * SUN-SUN", 3, Utc(2026, 11, 23)));
    }

    // ------------------------------------------------------------ Quartz weekdays

    /// <summary>The weekdays a pattern runs on in the week of Monday 2026-11-23, Monday first.</summary>
    private static DayOfWeek[] WeekdaysInWeek(Cron cron) =>
        cron.GetOccurrences(Utc(2026, 11, 23), Utc(2026, 11, 30), TimeZoneInfo.Utc).Select(d => d.DayOfWeek).ToArray();

    private static readonly DayOfWeek[] AllWeek =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    public static TheoryData<string, DayOfWeek[]> RangesEndingInSunday => new()
    {
        { "SAT-SUN", new[] { DayOfWeek.Saturday, DayOfWeek.Sunday } },
        { "FRI-SUN", new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday } },
        { "MON-SUN", AllWeek },
        { "SUN-SAT", AllWeek },
        { "MON-FRI", AllWeek.Take(5).ToArray() },
        { "SAT-SUN,WED", new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday, DayOfWeek.Sunday } },
        { "sat-sun", new[] { DayOfWeek.Saturday, DayOfWeek.Sunday } },
    };

    [Theory]
    [MemberData(nameof(RangesEndingInSunday))]
    public void WeekdayRangesEndingInSundayInBothModes(string field, DayOfWeek[] expected)
    {
        Assert.Equal(expected, WeekdaysInWeek(Cron.Parse($"0 0 * * {field}")));
        Assert.Equal(expected, WeekdaysInWeek(Cron.Parse($"0 0 * * {field}", Quartz)));
    }

    [Fact]
    public void SatSunNthInBothModes()
    {
        foreach (var options in new[] { CronParserOptions.Default, Quartz })
        {
            Assert.Equal(new[] { "2026-12-12", "2026-12-13", "2027-01-09", "2027-01-10" },
                Days("0 0 * * SAT-SUN#2", 4, Utc(2026, 12, 1), options));
        }
    }

    public static TheoryData<string, DayOfWeek[]> QuartzWrappingRanges => new()
    {
        { "FRI-MON", new[] { DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday } },
        { "7-1", new[] { DayOfWeek.Saturday, DayOfWeek.Sunday } },
        { "6-2", new[] { DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday } },
        { "FRI-TUE/2", new[] { DayOfWeek.Tuesday, DayOfWeek.Friday, DayOfWeek.Sunday } },
        { "7-1/1", new[] { DayOfWeek.Saturday, DayOfWeek.Sunday } },
    };

    [Theory]
    [MemberData(nameof(QuartzWrappingRanges))]
    public void QuartzWeekdayRangesWrapAroundTheWeek(string field, DayOfWeek[] expected) =>
        Assert.Equal(expected, WeekdaysInWeek(Cron.Parse($"0 0 * * {field}", Quartz)));

    [Theory]
    [InlineData("0 0 * * 8-1")]
    [InlineData("0 0 * * 7-0")]
    [InlineData("0 0 * * 0-7")]
    [InlineData("0 0 * * 8")]
    [InlineData("0 0 * * 1-8")]
    [InlineData("0 0 * * 0")]
    [InlineData("0 23-1 * * *")]
    [InlineData("0 0 30-2 * *")]
    [InlineData("0 0 * 12-1 *")]
    public void QuartzRejectsOutOfRangeWeekdaysAndOtherWrappingFields(string pattern) =>
        Assert.Throws<CronException>(() => Cron.Parse(pattern, Quartz));

    [Theory]
    [InlineData("FRI-MON")]
    [InlineData("6-0")]
    [InlineData("5-1")]
    [InlineData("FRI-TUE/2")]
    public void StandardWeekdayRangesDoNotWrap(string field) =>
        Assert.Throws<CronException>(() => Cron.Parse($"0 0 * * {field}"));

    [Fact]
    public void WeeklyNicknameRunsOnSundaysInBothModes()
    {
        foreach (var options in new[] { CronParserOptions.Default, Quartz })
        {
            Assert.Equal(new[] { DayOfWeek.Sunday }, WeekdaysInWeek(Cron.Parse("@weekly", options)));
        }
    }

    // ------------------------------------------------------------ fixed-time rule

    [Theory]
    [InlineData("0 30 2 * * *", FixedTimeRule.SingleValue, JobType.FixedTime)]
    [InlineData("30 2,14 * * *", FixedTimeRule.SingleValue, JobType.IntervalWildcard)]
    [InlineData("30 2,14 * * *", FixedTimeRule.NoWildcard, JobType.FixedTime)]
    [InlineData("30 2-3 * * *", FixedTimeRule.NoWildcard, JobType.FixedTime)]
    [InlineData("0,30 30 2 * * *", FixedTimeRule.NoWildcard, JobType.FixedTime)]
    [InlineData("*/30 2 * * *", FixedTimeRule.NoWildcard, JobType.IntervalWildcard)]
    [InlineData("0 0-23/2 * * *", FixedTimeRule.NoWildcard, JobType.IntervalWildcard)]
    [InlineData("30 * * * *", FixedTimeRule.NoWildcard, JobType.IntervalWildcard)]
    [InlineData("* 30 2 * * *", FixedTimeRule.NoWildcard, JobType.IntervalWildcard)]
    [InlineData("* * * * *", FixedTimeRule.All, JobType.FixedTime)]
    [InlineData("*/30 * * * *", FixedTimeRule.All, JobType.FixedTime)]
    [InlineData("0 */2 * * *", FixedTimeRule.All, JobType.FixedTime)]
    [InlineData("*/10 * * * * *", FixedTimeRule.All, JobType.FixedTime)]
    public void FixedTimeRuleDecidesTheJobType(string pattern, FixedTimeRule rule, JobType expected) =>
        Assert.Equal(expected, Cron.Parse(pattern, new CronParserOptions { FixedTimeRule = rule }).JobType);

    [Fact]
    public void FixedTimeRuleDefaultsToSingleValueAndIsPartOfEquality()
    {
        Assert.Equal(FixedTimeRule.SingleValue, CronParserOptions.Default.FixedTimeRule);
        Assert.Equal(FixedTimeRule.SingleValue, Cron.Parse("0 1,13 * * *").Pattern.FixedTimeRule);
        var a = Cron.Parse("0 1,13 * * *");
        var b = Cron.Parse("0 1,13 * * *", new CronParserOptions { FixedTimeRule = FixedTimeRule.NoWildcard });
        Assert.NotEqual(a, b);
        Assert.Equal(b, Cron.Parse("0 1,13 * * *", new CronParserOptions { FixedTimeRule = FixedTimeRule.NoWildcard }));
    }
}
