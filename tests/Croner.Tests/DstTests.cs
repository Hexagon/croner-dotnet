using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Croner.Tests.H;

namespace Croner.Tests;

/// <summary>Daylight saving time handling, ported from croner-rust.</summary>
public class DstTests
{
    private const string Stockholm = "Europe/Stockholm";
    private const string Paris = "Europe/Paris";

    private static TimeZoneInfo Zone(string id) => Tz(id);

    /// <summary>Asserts the same instant and the same UTC offset (DateTimeOffset equality ignores the offset).</summary>
    private static void Same(DateTimeOffset expected, DateTimeOffset actual)
    {
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Offset, actual.Offset);
    }

    private static void Same(IEnumerable<DateTimeOffset> expected, IEnumerable<DateTimeOffset> actual)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        Assert.Equal(e.Count, a.Count);
        for (int i = 0; i < e.Count; i++)
        {
            Same(e[i], a[i]);
        }
    }

    private static void MovesOneWay(IReadOnlyList<DateTimeOffset> times, Direction direction)
    {
        for (int i = 1; i < times.Count; i++)
        {
            bool ok = direction == Direction.Forward ? times[i - 1] < times[i] : times[i - 1] > times[i];
            Assert.True(ok, $"{direction} search went the other way, from {times[i - 1]} to {times[i]}");
        }
    }

    // Europe/Paris on 2024-10-27: 03:00 CEST goes back to 02:00 CET.
    private static DateTimeOffset Cest(int h, int m, int s = 0) =>
        h == 2 ? Ambiguous(Paris, true, 2024, 10, 27, h, m, s) : At(Paris, 2024, 10, 27, h, m, s);

    private static DateTimeOffset Cet(int h, int m, int s = 0) =>
        h == 2 ? Ambiguous(Paris, false, 2024, 10, 27, h, m, s) : At(Paris, 2024, 10, 27, h, m, s);

    // ---------------------------------------------------------------- gap

    [Fact]
    public void GapFixedTimeJobRunsAfterTheGap()
    {
        var cron = Cron.Parse("0 30 2 * * *");
        var next = cron.GetNextOccurrence(At(Stockholm, 2025, 3, 30, 1, 59, 59), Zone(Stockholm));
        Same(At(Stockholm, 2025, 3, 30, 3), next);
    }

    [Fact]
    public void GapIntervalJobSkipsTheGap()
    {
        Same(At(Stockholm, 2025, 3, 30, 3),
            Cron.Parse("0 */5 * * * *").GetNextOccurrence(At(Stockholm, 2025, 3, 30, 1, 59, 59), Zone(Stockholm)));
        Same(At(Stockholm, 2025, 3, 30, 3),
            Cron.Parse("* * * * * *").GetNextOccurrence(At(Stockholm, 2025, 3, 30, 1, 59, 59), Zone(Stockholm)));
    }

    [Fact]
    public void GapBackwardUsesPostGapInstant()
    {
        var cron = Cron.Parse("0 30 2 * * *");
        var gapRun = At(Stockholm, 2025, 3, 30, 3);
        Same(gapRun, cron.GetPreviousOccurrence(At(Stockholm, 2025, 3, 30, 4), Zone(Stockholm)));
        Same(At(Stockholm, 2025, 3, 29, 2, 30), cron.GetPreviousOccurrence(gapRun, Zone(Stockholm)));
    }

    [Fact]
    public void BackwardIterationCrossesTheGapWithoutInventingTimes()
    {
        var runs = Cron.Parse("0 * * * * *").GetPreviousOccurrences(At(Stockholm, 2025, 3, 30, 3, 2), Zone(Stockholm)).Take(4);
        Same(new[]
        {
            At(Stockholm, 2025, 3, 30, 3, 1), At(Stockholm, 2025, 3, 30, 3, 0),
            At(Stockholm, 2025, 3, 30, 1, 59), At(Stockholm, 2025, 3, 30, 1, 58),
        }, runs);
    }

    [Fact]
    public async Task GapAcrossMidnightCannotTrapABackwardSearch()
    {
        const string toronto = "America/Toronto";
        var cron = Cron.Parse("0 45 23 30 3 *");
        var work = Task.Run(() => (
            cron.GetPreviousOccurrence(At(toronto, 1919, 4, 15, 12), Zone(toronto)),
            cron.GetNextOccurrence(At(toronto, 1919, 3, 25, 12), Zone(toronto))));
        Assert.Same(work, await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20))));
        var (backward, forward) = await work;

        // IANA data (Linux, macOS) skips 23:30-00:30 on 1919-03-30, so 23:45 that day never happens.
        // Windows' own zone data has no 1919 history, so there the time exists and is the answer.
        bool skipped1919 = Zone(toronto).IsInvalidTime(D(1919, 3, 30, 23, 45));
        Same(At(toronto, skipped1919 ? 1918 : 1919, 3, 30, 23, 45), backward);
        Same(At(toronto, skipped1919 ? 1920 : 1919, 3, 30, 23, 45), forward);
    }

    [Fact]
    public void DailyMidnightRunsOnceOnSpringForwardDay()
    {
        const string london = "Europe/London";
        var runs = Cron.Parse("0 0 * * *").GetNextOccurrences(At(london, 2026, 3, 28, 12), Zone(london)).Take(3);
        Same(new[] { At(london, 2026, 3, 29), At(london, 2026, 3, 30), At(london, 2026, 3, 31) }, runs);
    }

    [Fact]
    public void StartInHourBeforeGapMovesToNextDay()
    {
        const string london = "Europe/London";
        Same(At(london, 2026, 3, 30),
            Cron.Parse("0 0 * * *").GetNextOccurrence(At(london, 2026, 3, 29, 0, 45), Zone(london)));
    }

    [Fact]
    public void JobInHourBeforeGapWithSecondsRunsOnce()
    {
        const string la = "America/Los_Angeles";
        var runs = Cron.Parse("0 0 1 * * *").GetNextOccurrences(At(la, 2026, 3, 7, 12), Zone(la)).Take(3);
        Same(new[] { At(la, 2026, 3, 8, 1), At(la, 2026, 3, 9, 1), At(la, 2026, 3, 10, 1) }, runs);
    }

    [Fact]
    public void BackwardSearchSkipsPostGapInstant()
    {
        const string london = "Europe/London";
        var cron = Cron.Parse("0 0 * * *");
        Same(At(london, 2026, 3, 29), cron.GetPreviousOccurrence(At(london, 2026, 3, 29, 2, 30), Zone(london)));
        Same(new[] { At(london, 2026, 3, 30), At(london, 2026, 3, 29), At(london, 2026, 3, 28) },
            cron.GetPreviousOccurrences(At(london, 2026, 3, 30), Zone(london), inclusive: true).Take(3));
    }

    [Fact]
    public void JobScheduledInsideGapStillRunsAfterIt()
    {
        const string london = "Europe/London";
        Same(At(london, 2026, 3, 29, 2),
            Cron.Parse("0 30 1 * * *").GetNextOccurrence(At(london, 2026, 3, 29, 0, 45), Zone(london)));
    }

    // ---------------------------------------------------------------- overlap

    [Fact]
    public void OverlapFixedTimeJobRunsAtFirstOccurrenceOnly()
    {
        var tz = Zone(Stockholm);
        var cron = Cron.Parse("0 30 2 * * *");
        Same(Ambiguous(Stockholm, true, 2025, 10, 26, 2, 30),
            cron.GetNextOccurrence(At(Stockholm, 2025, 10, 26, 1, 59, 59), tz));
        Same(At(Stockholm, 2025, 10, 27, 2, 30), cron.GetNextOccurrence(At(Stockholm, 2025, 10, 26, 3), tz));
    }

    [Fact]
    public void OverlapIntervalJobRunsOnBothOccurrences()
    {
        var tz = Zone(Stockholm);
        var runs = Cron.Parse("0 * * * * *").GetNextOccurrences(At(Stockholm, 2025, 10, 26, 1, 59, 59), tz).Take(120).ToList();
        Assert.Equal(120, runs.Count);
        for (int m = 0; m < 60; m++)
        {
            Same(Ambiguous(Stockholm, true, 2025, 10, 26, 2, m), runs[m]);
            Same(Ambiguous(Stockholm, false, 2025, 10, 26, 2, m), runs[60 + m]);
        }
        MovesOneWay(runs, Direction.Forward);
    }

    [Fact]
    public void OverlapHourStep()
    {
        var runs = Cron.Parse("0 0 */2 * * *").GetNextOccurrences(At(Stockholm, 2025, 10, 26), Zone(Stockholm), inclusive: true).Take(4);
        Same(new[]
        {
            At(Stockholm, 2025, 10, 26, 0),
            Ambiguous(Stockholm, true, 2025, 10, 26, 2),
            Ambiguous(Stockholm, false, 2025, 10, 26, 2),
            At(Stockholm, 2025, 10, 26, 4),
        }, runs);
    }

    [Fact]
    public void OverlapSearchStartsFromTheHalfItIsGiven()
    {
        var tz = Zone(Paris);
        var cron = Cron.Parse("* * * * *");
        Same(Cet(2, 31), cron.GetNextOccurrence(Cet(2, 30), tz));
        Same(Cest(2, 31), cron.GetNextOccurrence(Cest(2, 30), tz));
        Same(Cet(2, 29), cron.GetPreviousOccurrence(Cet(2, 30), tz));
        Same(Cest(2, 29), cron.GetPreviousOccurrence(Cest(2, 30), tz));
        Same(Cet(2, 0), cron.GetNextOccurrence(Cest(2, 59), tz));
        Same(Cest(2, 59), cron.GetPreviousOccurrence(Cet(2, 0), tz));
        Same(Cest(2, 0), cron.GetNextOccurrence(Cest(1, 59), tz));
        Same(Cet(2, 59), cron.GetPreviousOccurrence(Cet(3, 0), tz));
    }

    [Fact]
    public void OverlapIteratorsRunThroughBothHalvesInOrder()
    {
        var tz = Zone(Paris);
        var cron = Cron.Parse("* * * * *");
        var forward = cron.GetNextOccurrences(Cest(1, 59), tz).Take(121).ToList();
        MovesOneWay(forward, Direction.Forward);
        Same(Cest(2, 0), forward[0]);
        Same(Cest(2, 59), forward[59]);
        Same(Cet(2, 0), forward[60]);
        Same(Cet(2, 59), forward[119]);
        Same(Cet(3, 0), forward[120]);

        var backward = cron.GetPreviousOccurrences(Cet(3, 0), tz).Take(121).ToList();
        MovesOneWay(backward, Direction.Backward);
        Same(Cet(2, 59), backward[0]);
        Same(Cet(2, 0), backward[59]);
        Same(Cest(2, 59), backward[60]);
        Same(Cest(2, 0), backward[119]);
        Same(Cest(1, 59), backward[120]);

        backward.Reverse();
        Same(forward.Take(120), backward.Skip(1));
    }

    [Fact]
    public void OverlapStartingMidRangeStaysOnItsOwnHalf()
    {
        var tz = Zone(Paris);
        var cron = Cron.Parse("*/15 * * * *");
        var forward = cron.GetNextOccurrences(Cest(2, 45), tz).Take(5).ToList();
        MovesOneWay(forward, Direction.Forward);
        Same(new[] { Cet(2, 0), Cet(2, 15), Cet(2, 30), Cet(2, 45), Cet(3, 0) }, forward);

        var backward = cron.GetPreviousOccurrences(Cet(2, 0), tz).Take(4).ToList();
        Same(new[] { Cest(2, 45), Cest(2, 30), Cest(2, 15), Cest(2, 0) }, backward);
    }

    [Fact]
    public void OverlapFixedTimeJobRunsOnceFromEitherHalf()
    {
        var tz = Zone(Paris);
        var cron = Cron.Parse("30 2 * * *");
        Same(Cest(2, 30), cron.GetNextOccurrence(Cest(1, 0), tz));
        var nextDay = At(Paris, 2024, 10, 28, 2, 30);
        Same(nextDay, cron.GetNextOccurrence(Cest(2, 30), tz));
        Same(nextDay, cron.GetNextOccurrence(Cet(2, 45), tz));
        Same(Cest(2, 30), cron.GetPreviousOccurrence(Cet(4, 0), tz));
        Same(nextDay, cron.GetNextOccurrence(Cet(2, 0), tz));
        Same(Cest(2, 30), cron.GetPreviousOccurrence(Cet(2, 15), tz));
    }

    [Fact]
    public void OverlapFixedTimeMatchInALaterOverlapStillCounts()
    {
        Same(Ambiguous(Paris, true, 2025, 10, 26, 2, 30),
            Cron.Parse("30 2 26 10 *").GetNextOccurrence(Cet(2, 45), Zone(Paris)));
    }

    [Fact]
    public void OverlapCrossingIgnoresAMatchInALaterOverlap()
    {
        Same(Cet(2, 0), Cron.Parse("*/20 2 26-27 10 *").GetNextOccurrence(Cest(2, 41), Zone(Paris)));
    }

    [Fact]
    public void OverlapMatchInALaterOverlapTakesItsOwnRealOrder()
    {
        Same(Ambiguous(Paris, true, 2025, 10, 26, 2, 0),
            Cron.Parse("*/30 2 26-27 10 *").GetNextOccurrence(Cet(2, 45), Zone(Paris)));
    }

    public static IEnumerable<object[]> Overlaps() => new[]
    {
        new object[] { "Europe/Paris", 2024, 10, 27, 2, 30 },
        new object[] { "Australia/Lord_Howe", 2024, 4, 7, 1, 40 },
        new object[] { "Pacific/Chatham", 2024, 4, 7, 3, 20 },
        new object[] { "America/New_York", 2024, 11, 3, 1, 15 },
    };

    [Theory]
    [MemberData(nameof(Overlaps))]
    public void SearchNeverAnswersWithATimeItHasPassed(string zone, int y, int mo, int d, int h, int mi)
    {
        var tz = Zone(zone);
        var cron = Cron.Parse("* * * * *");
        var second = Ambiguous(zone, false, y, mo, d, h, mi);
        Assert.True(cron.GetNextOccurrence(second, tz) > second);
        var first = Ambiguous(zone, true, y, mo, d, h, mi);
        Assert.True(cron.GetPreviousOccurrence(first, tz) < first);
    }

    [Theory]
    [MemberData(nameof(Overlaps))]
    public void IteratorsRunOneWayAndCoverEveryInstantOnce(string zone, int y, int mo, int d, int h, int mi)
    {
        var tz = Zone(zone);
        var cron = Cron.Parse("* * * * *");
        var forward = cron.GetNextOccurrences(Ambiguous(zone, true, y, mo, d, h, mi), tz).Take(200).ToList();
        MovesOneWay(forward, Direction.Forward);
        var backward = cron.GetPreviousOccurrences(forward[^1], tz).Take(199).ToList();
        MovesOneWay(backward, Direction.Backward);
        backward.Reverse();
        Same(forward.Take(199), backward);
    }

    [Theory]
    [MemberData(nameof(Overlaps))]
    public void ADuplicatedMinuteComesRoundExactlyTwice(string zone, int y, int mo, int d, int h, int mi)
    {
        var tz = Zone(zone);
        var first = Ambiguous(zone, true, y, mo, d, h, mi);
        int hits = Cron.Parse("* * * * *").GetNextOccurrences(first, tz, inclusive: true).Take(201)
            .Count(t => t.DateTime == first.DateTime);
        Assert.Equal(2, hits);
    }
}
