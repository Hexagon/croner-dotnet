using System;
using System.Linq;
using static Croner.Tests.H;

namespace Croner.Tests;

public class MatchTests
{
    [Fact]
    public void IsTimeMatching()
    {
        var cron = Cron.Parse("0 9 1 1 *");
        Assert.True(cron.IsTimeMatching(D(2023, 1, 1, 9)));
        Assert.False(cron.IsTimeMatching(D(2023, 1, 1, 10)));
    }

    [Fact]
    public void LastDayOfFebruary()
    {
        var cron = Cron.Parse("0 9 L 2 *");
        Assert.True(cron.IsTimeMatching(D(2023, 2, 28, 9)));
        Assert.False(cron.IsTimeMatching(D(2023, 2, 28, 10)));
        Assert.False(cron.IsTimeMatching(D(2023, 2, 27, 9)));
        Assert.True(cron.IsTimeMatching(D(2024, 2, 29, 9)));
        Assert.False(cron.IsTimeMatching(D(2024, 2, 28, 9)));
    }

    [Theory]
    [InlineData("0 0 * * FRI#L")]
    [InlineData("0 0 * * FRIl")]
    [InlineData("0 0 * * 5L")]
    public void LastFridayOfYear(string pattern)
    {
        Assert.True(Cron.Parse(pattern).IsTimeMatching(D(2023, 12, 29)));
        Assert.False(Cron.Parse(pattern).IsTimeMatching(D(2023, 12, 22)));
    }

    [Fact]
    public void DifferentOffsetsMatchOnWallClock()
    {
        var cron = Cron.Parse("0 12 * * *");
        Assert.True(cron.IsTimeMatching(new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.FromHours(1))));
        Assert.True(cron.IsTimeMatching(new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.FromHours(-1))));
        var utc = new DateTimeOffset(2023, 1, 1, 11, 0, 0, TimeSpan.Zero);
        Assert.True(cron.IsTimeMatching(utc, Tz("Europe/Stockholm")));
        Assert.False(cron.IsTimeMatching(utc));
    }

    [Fact]
    public void LogicalOrForDateFields()
    {
        var cron = Cron.Parse("0 12 1 * MON");
        Assert.True(cron.IsTimeMatching(D(2025, 10, 1, 12))); // Wednesday the 1st
        Assert.True(cron.IsTimeMatching(D(2025, 10, 6, 12))); // a Monday
        Assert.False(cron.IsTimeMatching(D(2025, 10, 7, 12)));
    }

    [Fact]
    public void AndModifier()
    {
        var cron = Cron.Parse("0 12 1 * +MON");
        Assert.True(cron.IsTimeMatching(D(2025, 9, 1, 12))); // Monday the 1st
        Assert.False(cron.IsTimeMatching(D(2025, 10, 1, 12)));
        Assert.False(cron.IsTimeMatching(D(2025, 9, 8, 12)));
    }

    [Fact]
    public void GlobalAndMode()
    {
        var cron = Cron.Parse("0 12 1 * MON", new CronParserOptions { DomAndDow = true });
        Assert.True(cron.IsTimeMatching(D(2025, 9, 1, 12)));
        Assert.False(cron.IsTimeMatching(D(2025, 10, 1, 12)));
        Assert.False(cron.IsTimeMatching(D(2025, 9, 8, 12)));
    }

    [Fact]
    public void LastDayAndWeekdayModifiers()
    {
        Assert.True(Cron.Parse("0 0 L * *").IsTimeMatching(D(2025, 7, 31)));
        Assert.False(Cron.Parse("0 0 L * *").IsTimeMatching(D(2025, 7, 30)));
        Assert.True(Cron.Parse("0 0 * * 5L").IsTimeMatching(D(2025, 7, 25)));
        Assert.False(Cron.Parse("0 0 * * 5L").IsTimeMatching(D(2025, 7, 18)));
        Assert.True(Cron.Parse("0 0 * * 2#2").IsTimeMatching(D(2025, 7, 8)));
        Assert.False(Cron.Parse("0 0 * * 2#2").IsTimeMatching(D(2025, 7, 1)));
        // July 5 2025 is a Saturday -> Friday 4th
        Assert.True(Cron.Parse("0 0 5W 7 *").IsTimeMatching(D(2025, 7, 4)));
    }

    [Fact]
    public void LastWeekday()
    {
        var cron = Cron.Parse("0 0 LW * *");
        Assert.True(cron.IsTimeMatching(D(2025, 2, 28)));
        Assert.True(cron.IsTimeMatching(D(2025, 8, 29)));
        Assert.False(cron.IsTimeMatching(D(2025, 8, 31)));
        Assert.True(cron.IsTimeMatching(D(2025, 11, 28)));
        Assert.False(cron.IsTimeMatching(D(2025, 11, 30)));
    }

    [Fact]
    public void ThirtyFirstWOnlyWhenTheMonthHasIt()
    {
        var cron = Cron.Parse("0 0 31W * *");
        Assert.True(cron.IsTimeMatching(D(2025, 1, 31)));
        Assert.False(cron.IsTimeMatching(D(2025, 2, 28)));
        Assert.False(cron.IsTimeMatching(D(2025, 4, 30)));
        Assert.True(cron.IsTimeMatching(D(2025, 5, 30)));
        Assert.True(cron.IsTimeMatching(D(2025, 8, 29)));
    }

    [Fact]
    public void FirstWOnSaturdayMovesToMonday()
    {
        // 2025-02-01 is a Saturday: 1W moves forward to Monday the 3rd
        var cron = Cron.Parse("0 0 1W * *");
        Assert.False(cron.IsTimeMatching(D(2025, 1, 31)));
        Assert.True(cron.IsTimeMatching(D(2025, 2, 3)));
    }
}

public class OccurrenceTests
{
    [Fact]
    public void NextSecond() =>
        Assert.Equal(D(2023, 1, 1, 0, 0, 30), Seconds("* * * * * *").FindNextOccurrence(D(2023, 1, 1, 0, 0, 29)));

    [Fact]
    public void NextMinute() =>
        Assert.Equal(D(2023, 1, 1, 0, 1, 0), Cron.Parse("* * * * *").FindNextOccurrence(D(2023, 1, 1, 0, 0, 29)));

    [Fact]
    public void WrapMonthAndYear() =>
        Assert.Equal(D(2024, 1, 1, 15), Seconds("0 0 15 * * *").FindNextOccurrence(D(2023, 12, 31, 16)));

    [Fact]
    public void WeekdayPattern()
    {
        var runs = Seconds("0 0 0 * * 5,6").IterAfter(D(2022, 2, 17)).Take(6).ToList();
        Assert.Equal(new[] { D(2022, 2, 18), D(2022, 2, 19), D(2022, 2, 25), D(2022, 2, 26), D(2022, 3, 4), D(2022, 3, 5) }, runs);
    }

    [Fact]
    public void WeekdayCombinedWithDayOfMonth()
    {
        var runs = Seconds("59 59 23 2 * 6").IterAfter(D(2022, 1, 31)).Take(6).Select(d => (d.Month, d.Day)).ToList();
        Assert.Equal(new[] { (2, 2), (2, 5), (2, 12), (2, 19), (2, 26), (3, 2) }, runs);
    }

    [Fact]
    public void WeekdayAlone()
    {
        var runs = Cron.Parse("15 9 * * mon").IterAfter(D(2022, 2, 28, 23, 59)).Take(3).ToList();
        Assert.Equal(new[] { D(2022, 3, 7, 9, 15), D(2022, 3, 14, 9, 15), D(2022, 3, 21, 9, 15) }, runs);
    }

    [Fact]
    public void ThirteenWOrWednesday()
    {
        var runs = Cron.Parse("0 0 13W * WED").IterFrom(D(2024, 1, 1), Direction.Forward).Take(5).ToList();
        Assert.Equal(new[] { D(2024, 1, 3), D(2024, 1, 10), D(2024, 1, 12), D(2024, 1, 17), D(2024, 1, 24) }, runs);
    }

    [Fact]
    public void ThirtyFirstDecemberFridayAnd()
    {
        var cron = Cron.Parse("0 0 0 31 12 FRI", new CronParserOptions { Seconds = SecondsMode.Required, DomAndDow = true });
        var runs = cron.IterFrom(D(2024, 1, 1), Direction.Forward).Take(5).ToList();
        Assert.Equal(new[] { D(2027, 12, 31), D(2032, 12, 31), D(2038, 12, 31), D(2049, 12, 31), D(2055, 12, 31) }, runs);
    }

    [Fact]
    public void InclusiveAndExclusive()
    {
        var cron = Seconds("59 59 23 * * *");
        Assert.Equal(D(2023, 3, 14, 23, 59, 59), cron.FindNextOccurrence(D(2023, 3, 14, 23, 59, 59), inclusive: true));
        Assert.Equal(D(2023, 3, 15, 23, 59, 59), cron.FindNextOccurrence(D(2023, 3, 14, 23, 59, 59)));
    }

    [Fact]
    public void LargeTimeJumps()
    {
        var run = Cron.Parse("0 0 * * *").IterAfter(D(2020, 1, 1)).ElementAt(365 * 5 + 1);
        Assert.Equal(D(2025, 1, 1), run);
    }

    [Fact]
    public void DifferentMonthLengths()
    {
        var cron = Cron.Parse("0 0 L * *");
        Assert.Equal(D(2023, 2, 28), cron.FindNextOccurrence(D(2023, 2, 1)));
        Assert.Equal(D(2024, 2, 29), cron.FindNextOccurrence(D(2024, 2, 1)));
        Assert.Equal(D(2023, 4, 30), cron.FindNextOccurrence(D(2023, 4, 1)));
    }

    [Fact]
    public void NonStandardIntervalsWithOffset()
    {
        var runs = Cron.Parse("7-59/29 2-23/13 * * *").IterAfter(D(2023, 1, 1)).Take(2).ToList();
        Assert.Equal(new[] { D(2023, 1, 1, 2, 7), D(2023, 1, 1, 2, 36) }, runs);
    }

    [Fact]
    public void UnusualEndOfMonthStartOfMonthMonday()
    {
        var runs = Cron.Parse("0 0 */31,1-7 */1 MON").IterFrom(D(2023, 12, 24), Direction.Forward).Take(13).ToList();
        Assert.Equal(new[]
        {
            D(2023, 12, 25), D(2024, 1, 1), D(2024, 1, 2), D(2024, 1, 3), D(2024, 1, 4), D(2024, 1, 5),
            D(2024, 1, 6), D(2024, 1, 7), D(2024, 1, 8), D(2024, 1, 15), D(2024, 1, 22), D(2024, 1, 29), D(2024, 2, 1),
        }, runs);
    }

    [Fact]
    public void LastWeekdayFindsEveryMonth()
    {
        var expected = new[]
        {
            D(2025, 1, 31), D(2025, 2, 28), D(2025, 3, 31), D(2025, 4, 30), D(2025, 5, 30), D(2025, 6, 30),
            D(2025, 7, 31), D(2025, 8, 29), D(2025, 9, 30), D(2025, 10, 31), D(2025, 11, 28), D(2025, 12, 31),
        };
        Assert.Equal(expected, Cron.Parse("0 0 LW * *").IterAfter(D(2025, 1, 1)).Take(12).ToList());
    }

    [Fact]
    public void Previous()
    {
        var cron = Cron.Parse("0 12 * * *");
        Assert.Equal(D(2024, 5, 9, 12), cron.FindPreviousOccurrence(D(2024, 5, 10, 11, 59)));
        Assert.Equal(D(2024, 5, 10, 12), cron.FindPreviousOccurrence(D(2024, 5, 10, 12, 0, 1)));
        Assert.Equal(D(2024, 5, 10, 12), cron.FindPreviousOccurrence(D(2024, 5, 10, 12), inclusive: true));
        Assert.Equal(D(2024, 5, 9, 12), cron.FindPreviousOccurrence(D(2024, 5, 10, 12)));
        Assert.Equal(D(2023, 12, 31), Cron.Parse("0 0 L * *").FindPreviousOccurrence(D(2024, 1, 15)));
    }

    [Fact]
    public void IteratorsAgree()
    {
        var cron = Cron.Parse("*/7 3-5 * * MON,FRI");
        var forward = cron.IterAfter(D(2024, 1, 1)).Take(100).ToList();
        var backward = cron.IterBefore(forward[^1]).Take(99).Reverse().ToList();
        Assert.Equal(forward.Take(99), backward);
    }

    [Fact]
    public void YearField()
    {
        var cron = Cron.Parse("0 0 0 1 1 * 2030");
        Assert.Equal(D(2030, 1, 1), cron.FindNextOccurrence(D(2024, 6, 1)));
        Assert.Equal(D(2030, 1, 1), cron.FindPreviousOccurrence(D(2040, 6, 1)));
        Assert.Single(cron.IterAfter(D(2024, 6, 1)));
    }

    [Fact]
    public void ImpossiblePatternThrows()
    {
        var ex = Assert.Throws<CronException>(() => Cron.Parse("0 0 30 2 *").FindNextOccurrence(D(2024, 1, 1)));
        Assert.Equal(CronErrorKind.TimeSearchLimitExceeded, ex.Kind);
        Assert.Empty(Cron.Parse("0 0 30 2 *").IterAfter(D(2024, 1, 1)));
        Assert.Empty(Cron.Parse("0 0 30 2 *").IterBefore(D(2024, 1, 1)));
        Assert.False(Cron.Parse("0 0 30 2 *").TryFindOccurrence(DateTimeOffset.UnixEpoch, null, false, Direction.Forward, out _));
    }

    [Fact]
    public void DateTimeKindsArePreserved()
    {
        var cron = Cron.Parse("0 0 * * *");
        var utc = cron.FindNextOccurrence(new DateTime(2024, 1, 1, 5, 0, 0, DateTimeKind.Utc));
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), utc);

        var naive = cron.FindNextOccurrence(D(2024, 1, 1, 5));
        Assert.Equal(DateTimeKind.Unspecified, naive.Kind);

        var local = cron.FindNextOccurrence(new DateTime(2024, 1, 1, 5, 0, 0, DateTimeKind.Local));
        Assert.Equal(DateTimeKind.Local, local.Kind);
        Assert.Equal(0, local.Hour);
    }

    [Fact]
    public void FixedOffsetSearch()
    {
        var start = new DateTimeOffset(2024, 1, 1, 23, 30, 0, TimeSpan.FromHours(5));
        var next = Cron.Parse("0 0 * * *").FindNextOccurrence(start);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.FromHours(5)), next);
        Assert.Equal(TimeSpan.FromHours(5), next.Offset);
    }

    [Fact]
    public void TimeZoneSearchReturnsZoneOffset()
    {
        var tz = Tz("America/New_York");
        var start = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero); // 20:00 on June 30 in New York
        var next = Cron.Parse("0 9 * * *").FindNextOccurrence(start, tz);
        Assert.Equal(new DateTimeOffset(2024, 7, 1, 9, 0, 0, TimeSpan.FromHours(-4)), next);
        Assert.Equal(TimeSpan.FromHours(-4), next.Offset);

        var winter = Cron.Parse("0 9 * * *").FindNextOccurrence(new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero), tz);
        Assert.Equal(TimeSpan.FromHours(-5), winter.Offset);
    }

    [Fact]
    public void SubSecondStart()
    {
        var cron = Seconds("* * * * * *");
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, 500, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 12, 0, 1, TimeSpan.Zero), cron.FindNextOccurrence(start));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 11, 59, 59, TimeSpan.Zero), cron.FindPreviousOccurrence(start));
    }
}
