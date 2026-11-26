using System;
using System.Linq;
using static Croner.Tests.H;

namespace Croner.Tests;

/// <summary>
/// DST cases ported from croner for JavaScript (<c>fixed-time-rule.test.ts</c>) and croner-rust
/// (<c>tests/fixed_time_rule.rs</c>, <c>tests/dst_gap_long.rs</c>), so the three give the same runs.
/// </summary>
public class DstParityTests
{
    private const string Luxembourg = "Europe/Luxembourg";
    private const string NewYork = "America/New_York";

    // 2026-03-29: 02:00 CET jumps to 03:00 CEST (01:00Z)
    private static readonly DateTimeOffset Spring = DateTimeOffset.Parse("2026-03-28T23:00:00Z");

    // 2026-10-25: 03:00 CEST goes back to 02:00 CET (01:00Z), 02:00-02:59 occurs twice
    private static readonly DateTimeOffset Autumn = DateTimeOffset.Parse("2026-10-24T22:00:00Z");

    private static Cron Parse(string pattern, FixedTimeRule rule = FixedTimeRule.SingleValue) =>
        Cron.Parse(pattern, new CronParserOptions { FixedTimeRule = rule });

    private static string[] Next(Cron cron, DateTimeOffset from, int count, string zone = Luxembourg) =>
        cron.GetNextOccurrences(from, Tz(zone)).Take(count).Select(Iso).ToArray();

    private static string[] Previous(Cron cron, DateTimeOffset from, int count, string zone = Luxembourg) =>
        cron.GetPreviousOccurrences(from, Tz(zone)).Take(count).Select(Iso).ToArray();

    private static string Iso(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'");

    /// <summary>Wall clock time with its offset, e.g. "2026-11-01 01:00:00 -04:00".</summary>
    private static string Local(DateTimeOffset d) => d.ToString("yyyy-MM-dd HH:mm:ss zzz");

    // ------------------------------------------------------------ fixedTimeRule, croner for JavaScript

    [Fact]
    public void DefaultRuleTreatsListsAsIntervalJobs()
    {
        var cron = Parse("30 2,14 * * *");
        // 02:30 is skipped in the gap, and runs twice in the overlap
        Assert.Equal(new[] { "2026-03-29T12:30:00Z" }, Next(cron, Spring, 1));
        Assert.Equal(new[] { "2026-10-25T00:30:00Z", "2026-10-25T01:30:00Z", "2026-10-25T13:30:00Z" }, Next(cron, Autumn, 3));
    }

    [Fact]
    public void NoWildcardListOfHoursIsFixedTime()
    {
        var cron = Parse("30 2,14 * * *", FixedTimeRule.NoWildcard);
        Assert.Equal(new[] { "2026-03-29T01:00:00Z", "2026-03-29T12:30:00Z" }, Next(cron, Spring, 2));
        Assert.Equal(new[] { "2026-10-25T00:30:00Z", "2026-10-25T13:30:00Z", "2026-10-26T01:30:00Z" }, Next(cron, Autumn, 3));
        // The same runs, backwards
        Assert.Equal(new[] { "2026-03-29T12:30:00Z", "2026-03-29T01:00:00Z" },
            Previous(cron, DateTimeOffset.Parse("2026-03-29T13:00:00Z"), 2));
        Assert.Equal(new[] { "2026-10-25T00:30:00Z", "2026-10-24T12:30:00Z" },
            Previous(cron, DateTimeOffset.Parse("2026-10-25T13:00:00Z"), 2));
    }

    [Fact]
    public void NoWildcardListOfMinutesIsFixedTime()
    {
        var cron = Parse("0,30 2 * * *", FixedTimeRule.NoWildcard);
        // Both times in the gap run at its end, once
        Assert.Equal(new[] { "2026-03-29T01:00:00Z", "2026-03-30T00:00:00Z" }, Next(cron, Spring, 2));
        Assert.Equal(new[] { "2026-10-25T00:00:00Z", "2026-10-25T00:30:00Z", "2026-10-26T01:00:00Z" }, Next(cron, Autumn, 3));
    }

    [Fact]
    public void NoWildcardRangeOfHoursIsFixedTime()
    {
        var cron = Parse("30 2-3 * * *", FixedTimeRule.NoWildcard);
        Assert.Equal(new[] { "2026-03-29T01:00:00Z", "2026-03-29T01:30:00Z" }, Next(cron, Spring, 2));
        Assert.Equal(new[] { "2026-10-25T00:30:00Z", "2026-10-25T02:30:00Z", "2026-10-26T01:30:00Z" }, Next(cron, Autumn, 3));
    }

    [Fact]
    public void NoWildcardTimesInAGapThatMeetAtItsEndRunOnce()
    {
        // 02:00 does not exist and 03:00 is the end of the gap, so both are 03:00 CEST
        Assert.Equal(new[] { "2026-03-29T01:00:00Z", "2026-03-30T00:00:00Z" },
            Next(Parse("0 2,3 * * *", FixedTimeRule.NoWildcard), Spring, 2));
    }

    [Theory]
    [InlineData("*/30 2 * * *")]
    [InlineData("0 0-23/2 * * *")]
    [InlineData("30 * * * *")]
    [InlineData("* 30 2 * * *")]
    [InlineData("30 2 * * *")]
    [InlineData("0 30 2 * * *")]
    public void NoWildcardKeepsWildcardsStepsAndSingleValuesAsBefore(string pattern)
    {
        Assert.Equal(Next(Parse(pattern), Autumn, 6), Next(Parse(pattern, FixedTimeRule.NoWildcard), Autumn, 6));
        Assert.Equal(Next(Parse(pattern), Spring, 6), Next(Parse(pattern, FixedTimeRule.NoWildcard), Spring, 6));
    }

    [Fact]
    public void NoWildcardStepIsStillAnIntervalJob()
    {
        Assert.Equal(new[] { "2026-10-25T00:00:00Z", "2026-10-25T01:00:00Z" },
            Next(Parse("0 0-23/2 * * *", FixedTimeRule.NoWildcard), Autumn, 2));
    }

    [Fact]
    public void NoWildcardListOfSecondsIsFixedTime()
    {
        Assert.Equal(new[] { "2026-10-25T00:30:00Z", "2026-10-25T00:30:30Z", "2026-10-26T01:30:00Z" },
            Next(Parse("0,30 30 2 * * *", FixedTimeRule.NoWildcard), Autumn, 3));
    }

    // ------------------------------------------------------------ FixedTimeRule.All, croner-rust

    [Fact]
    public void AllRuleRunsARepeatedHourOnce()
    {
        // Clocks in America/New_York go back from 02:00 EDT to 01:00 EST on 2026-11-01.
        var runs = Parse("*/30 * * * *", FixedTimeRule.All).GetNextOccurrences(At(NewYork, 2026, 11, 1, 0, 50), Tz(NewYork))
            .Take(5).Select(Local);
        Assert.Equal(new[]
        {
            "2026-11-01 01:00:00 -04:00",
            "2026-11-01 01:30:00 -04:00",
            "2026-11-01 02:00:00 -05:00",
            "2026-11-01 02:30:00 -05:00",
            "2026-11-01 03:00:00 -05:00",
        }, runs);
    }

    [Fact]
    public void AllRuleNeverReturnsAPastTimeFromTheSecondPass()
    {
        // 01:05 EST is on the second pass; croner 2.x returned 01:30 EDT here, which lies before the start.
        var start = Ambiguous(NewYork, false, 2026, 11, 1, 1, 5);
        var runs = Parse("*/30 * * * *", FixedTimeRule.All).GetNextOccurrences(start, Tz(NewYork)).Take(2).Select(Local);
        Assert.Equal(new[] { "2026-11-01 02:00:00 -05:00", "2026-11-01 02:30:00 -05:00" }, runs);
    }

    [Fact]
    public void AllRuleRunsOnceAfterAGap()
    {
        // Clocks in America/New_York jump from 02:00 EST to 03:00 EDT on 2026-03-08.
        var runs = Parse("*/30 2 * * *", FixedTimeRule.All).GetNextOccurrences(At(NewYork, 2026, 3, 7, 12), Tz(NewYork))
            .Take(3).Select(Local);
        Assert.Equal(new[] { "2026-03-08 03:00:00 -04:00", "2026-03-09 02:00:00 -04:00", "2026-03-09 02:30:00 -04:00" }, runs);

        runs = Parse("*/30 * * * *", FixedTimeRule.All).GetNextOccurrences(At(NewYork, 2026, 3, 8, 1, 10), Tz(NewYork))
            .Take(3).Select(Local);
        Assert.Equal(new[] { "2026-03-08 01:30:00 -05:00", "2026-03-08 03:00:00 -04:00", "2026-03-08 03:30:00 -04:00" }, runs);
    }

    // ------------------------------------------------------------ gaps longer than two hours

    // Pacific/Apia skipped the whole of 30 December 2011: 2011-12-29 23:59:59 -10:00 was followed by
    // 2011-12-31 00:00:00 +14:00. Zone data without that history (Windows) has no gap to test.
    private const string Apia = "Pacific/Apia";

    private static bool ApiaSkips30December2011()
    {
        try
        {
            // TimeZoneInfo.IsInvalidTime misses this gap, as it changed the base offset; the UTC conversion does not.
            var zone = Tz(Apia);
            return TimeZoneInfo.ConvertTime(DateTimeOffset.Parse("2011-12-30T09:59:59Z"), zone).Day == 29
                && TimeZoneInfo.ConvertTime(DateTimeOffset.Parse("2011-12-30T10:00:00Z"), zone).Day == 31;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }

    private static DateTimeOffset InApia(int y, int mo, int d, int h, int mi = 0) =>
        // Every date used here is in Samoan summer time: -10:00 before the skipped day, +14:00 after it.
        new(y, mo, d, h, mi, 0, TimeSpan.FromHours(new DateTime(y, mo, d) < new DateTime(2011, 12, 30) ? -10 : 14));

    [Fact]
    public void FixedTimeJobsCrossADayLongGap()
    {
        if (!ApiaSkips30December2011())
        {
            return;
        }
        var zone = Tz(Apia);
        var afterGap = InApia(2011, 12, 31, 0);
        var before = InApia(2011, 12, 29, 23, 30);
        var after = InApia(2011, 12, 31, 0, 30);

        foreach (string pattern in new[] { "0 12 * * *", "0 0 * * *", "0 * * * *" })
        {
            Assert.Equal(afterGap, Cron.Parse(pattern).GetNextOccurrence(before, zone));
            Assert.Equal(afterGap, Cron.Parse(pattern).GetPreviousOccurrence(after, zone));
        }

        // A job restricted to the skipped day skips it
        Assert.Equal(InApia(2012, 1, 30, 12), Cron.Parse("0 12 30 * *").GetNextOccurrence(before, zone));
        Assert.Equal(InApia(2011, 11, 30, 12), Cron.Parse("0 12 30 * *").GetPreviousOccurrence(after, zone));

        // The run before the moved 30 December run is the regular 29 December one
        Assert.Equal(InApia(2011, 12, 29, 12), Cron.Parse("0 12 * * *").GetPreviousOccurrence(afterGap, zone));

        Assert.Equal(new[] { InApia(2011, 12, 29, 12), afterGap, InApia(2011, 12, 31, 12) },
            Cron.Parse("0 12 * * *").GetNextOccurrences(InApia(2011, 12, 28, 13), zone).Take(3));
    }

    // ------------------------------------------------------------ a gap that ends at midnight

    // America/Nuuk on 2024-03-30: 23:00 -02:00 jumps to 00:00 -01:00 on the 31st.
    private const string Nuuk = "America/Nuuk";

    [Fact]
    public void GapEndingAtMidnightMovesTheRunToTheNextDayOnlyWhenTheDayMatches()
    {
        TimeZoneInfo zone;
        try
        {
            zone = Tz(Nuuk);
        }
        catch (TimeZoneNotFoundException)
        {
            return;
        }
        if (!zone.IsInvalidTime(D(2024, 3, 30, 23, 30)))
        {
            return;
        }

        var start = At(Nuuk, 2024, 3, 30, 12);
        // A daily job runs at the end of the gap, on the next calendar day
        Assert.Equal(At(Nuuk, 2024, 3, 31, 0), Cron.Parse("30 23 * * *").GetNextOccurrence(start, zone));
        // A job restricted to the 30th has no run in March 2024
        Assert.Equal(At(Nuuk, 2024, 4, 30, 23, 30), Cron.Parse("30 23 30 * *").GetNextOccurrence(start, zone));
    }
}
