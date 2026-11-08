using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Croner.Tests.H;

namespace Croner.Tests;

public class CronJobTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0, int ms = 0) =>
        new(y, mo, d, h, mi, s, ms, TimeSpan.Zero);

    private static CronJobOptions InUtc(CronJobOptions? o = null) => (o ?? new CronJobOptions()) with { UtcOffset = TimeSpan.Zero };

    [Fact]
    public void NextRunWithoutCallback()
    {
        var job = new CronJob("0 0 12 * * *", InUtc());
        Assert.Equal(Utc(2025, 1, 15, 12), job.GetNextRun(Utc(2025, 1, 15)));
        Assert.False(job.IsScheduled);
        Assert.Equal("0 0 12 * * *", job.Cron?.ToString());
        Assert.Null(job.RunOnceAt);
    }

    [Theory]
    [InlineData(-1, "2025-01-14T12:00:00Z")]
    [InlineData(1, "2025-01-16T12:00:00Z")]
    [InlineData(-2, "2025-01-13T12:00:00Z")]
    public void DayOffset(int offset, string expected)
    {
        var job = new CronJob("0 0 12 * * WED", InUtc(new CronJobOptions { DayOffset = offset }));
        Assert.Equal(DateTimeOffset.Parse(expected), job.GetNextRun(Utc(2025, 1, 13)));
    }

    [Fact]
    public void DayOffsetFirstSaturday()
    {
        var job = new CronJob("0 8 * * 6#1", InUtc(new CronJobOptions { DayOffset = -1 }));
        var next = job.GetNextRun(Utc(2025, 1, 1));
        Assert.Equal(Utc(2025, 1, 3, 8), next);
        Assert.Equal(DayOfWeek.Friday, next!.Value.DayOfWeek);
    }

    [Fact]
    public void DayOffsetAcrossBoundariesAndRuns()
    {
        Assert.Equal(Utc(2024, 12, 31, 12), new CronJob("0 0 12 1 * *", InUtc(new CronJobOptions { DayOffset = -1 })).GetNextRun(Utc(2024, 12, 31)));
        Assert.Equal(Utc(2025, 2, 1, 12), new CronJob("0 0 12 31 1 *", InUtc(new CronJobOptions { DayOffset = 1 })).GetNextRun(Utc(2025, 1, 1)));
        var runs = new CronJob("0 0 12 * * WED", InUtc(new CronJobOptions { DayOffset = -1 })).GetNextRuns(Utc(2025, 1, 13)).Take(3);
        Assert.Equal(new[] { Utc(2025, 1, 14, 12), Utc(2025, 1, 21, 12), Utc(2025, 1, 28, 12) }, runs);
        Assert.Equal(Utc(2025, 1, 30, 12), new CronJob("0 0 12 L * *", InUtc(new CronJobOptions { DayOffset = -1 })).GetNextRun(Utc(2025, 1, 1)));
    }

    [Fact]
    public void Interval()
    {
        var job = new CronJob("0,30 * * * * *", InUtc(new CronJobOptions { MinimumInterval = TimeSpan.FromSeconds(90) }));
        var runs = job.GetNextRuns(Utc(2022, 2, 16)).Take(3);
        Assert.Equal(new[] { Utc(2022, 2, 16, 0, 1, 30), Utc(2022, 2, 16, 0, 3, 0), Utc(2022, 2, 16, 0, 4, 30) }, runs);
    }

    [Fact]
    public void IntervalWithStartAtInThePastAndFuture()
    {
        var clock = new ManualTime(Utc(2025, 6, 10, 10));
        var week = TimeSpan.FromDays(7);
        var past = new CronJob("* * * * * *", InUtc(new CronJobOptions { MinimumInterval = week, StartAt = Utc(2025, 6, 9, 19, 31, 2), TimeProvider = clock }));
        Assert.Equal(Utc(2025, 6, 16, 19, 31, 2), past.GetNextRun());

        var future = new CronJob("* * * * * *", InUtc(new CronJobOptions { MinimumInterval = week, StartAt = Utc(2025, 6, 11, 0, 31, 2), TimeProvider = clock }));
        Assert.Equal(Utc(2025, 6, 11, 0, 31, 2), future.GetNextRun());
    }

    [Theory]
    [InlineData("0 0 0 1 1 * 2020")]
    [InlineData("0 0 0 29 2 * 2021")]
    public void ExhaustedSchedule(string pattern)
    {
        var opts = new CronJobOptions { StartAt = Utc(2019, 1, 1), MinimumInterval = TimeSpan.FromSeconds(60), TimeZone = TimeZoneInfo.Utc };
        var job = new CronJob(pattern, opts);
        Assert.Null(job.GetNextRun());
        Assert.Empty(job.GetNextRuns());
        Assert.Null(job.GetTimeToNextRun());

        using var scheduled = new CronJob(pattern, _ => throw new InvalidOperationException("must not run"), opts);
        Assert.False(scheduled.IsScheduled);
        Assert.Null(scheduled.CurrentRun);
    }

    [Fact]
    public void MaxRunsLimitsNextRuns()
    {
        Assert.Equal(5, new CronJob("* * * * * *", new CronJobOptions { MaxRuns = 5 }).GetNextRuns().Take(10).Count());
        Assert.Equal(5, new CronJob("* * * * * *", new CronJobOptions { MaxRuns = 5 }).GetNextRuns().Count());
    }

    [Fact]
    public void StartAtIsInclusive()
    {
        var job = new CronJob("0 0 12 * * *", new CronJobOptions { TimeZone = TimeZoneInfo.Utc, StartAt = Utc(2025, 1, 1, 12) });
        Assert.Equal(Utc(2025, 1, 1, 12), job.GetNextRun(Utc(2025, 1, 1)));
        Assert.Equal(Utc(2025, 1, 2, 12), job.GetNextRun(Utc(2025, 1, 1, 12)));
    }

    [Fact]
    public void StartAtBetweenSecondsStartsAtNextSecond()
    {
        var startAt = Utc(2026, 6, 10, 12, 0, 0, 400);
        foreach (var opts in new[] { new CronJobOptions { UtcOffset = TimeSpan.Zero }, new CronJobOptions { TimeZone = Tz("Europe/Stockholm") }, new CronJobOptions() })
        {
            var job = new CronJob("* * * * * *", opts with { StartAt = startAt });
            Assert.Equal(Utc(2026, 6, 10, 12, 0, 1), job.GetNextRun(Utc(2026, 6, 1)));
        }
        Assert.Equal(Utc(2026, 6, 11, 12), new CronJob("0 0 12 * * *", InUtc(new CronJobOptions { StartAt = startAt })).GetNextRun(Utc(2026, 6, 1)));
    }

    [Fact]
    public void StopAtBetweenSecondsIncludesTheSecondBefore()
    {
        var job = new CronJob("* * * * * *", new CronJobOptions { TimeZone = Tz("Europe/Stockholm"), StopAt = Utc(2026, 6, 10, 12, 0, 0, 400) });
        Assert.Equal(new[] { Utc(2026, 6, 10, 11, 59, 59), Utc(2026, 6, 10, 12) }, job.GetNextRuns(Utc(2026, 6, 10, 11, 59, 58, 500)).Take(2));
        Assert.Equal(2, job.GetNextRuns(Utc(2026, 6, 10, 11, 59, 58, 500)).Take(5).Count());
    }

    [Fact]
    public void StopAtInThePast()
    {
        var clock = new ManualTime(Utc(2025, 6, 10, 10));
        Assert.Null(new CronJob("0 0 12 * * *", new CronJobOptions { StopAt = Utc(2025, 6, 9), TimeProvider = clock }).GetNextRun());
    }

    [Fact]
    public void TimeZoneOption()
    {
        var job = new CronJob("0 0 9 * * *", new CronJobOptions { TimeZone = Tz("America/New_York") });
        var next = job.GetNextRun(Utc(2025, 7, 1));
        Assert.Equal(Utc(2025, 7, 1, 13), next);
        Assert.Equal(TimeSpan.FromHours(-4), next!.Value.Offset);
    }

    [Fact]
    public void UtcOffsetOption()
    {
        var job = new CronJob("0 0 12 * * *", new CronJobOptions { UtcOffset = TimeSpan.FromHours(2), StartAt = new DateTimeOffset(2025, 1, 2, 12, 0, 0, TimeSpan.FromHours(2)) });
        Assert.Equal(Utc(2025, 1, 2, 10), job.GetNextRun(Utc(2025, 1, 1)));
    }

    [Fact]
    public void InvalidOptionsThrow()
    {
        Assert.Throws<ArgumentException>(() => new CronJob("* * * * *", new CronJobOptions { UtcOffset = TimeSpan.Zero, TimeZone = TimeZoneInfo.Utc }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CronJob("* * * * *", new CronJobOptions { UtcOffset = TimeSpan.FromHours(15) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CronJob("* * * * *", new CronJobOptions { MinimumInterval = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<CronException>(() => new CronJob("not a pattern"));
    }

    [Theory]
    [InlineData(SecondsMode.Disallowed, YearMode.Disallowed, "* * * * *", true)]
    [InlineData(SecondsMode.Disallowed, YearMode.Disallowed, "* * * * * *", false)]
    [InlineData(SecondsMode.Required, YearMode.Disallowed, "* * * * * *", true)]
    [InlineData(SecondsMode.Required, YearMode.Disallowed, "* * * * *", false)]
    [InlineData(SecondsMode.Required, YearMode.Required, "* * * * * * *", true)]
    [InlineData(SecondsMode.Required, YearMode.Required, "* * * * * *", false)]
    public void ParserOptions(SecondsMode seconds, YearMode year, string pattern, bool valid)
    {
        var opts = new CronJobOptions { Parser = new CronParserOptions { Seconds = seconds, Year = year } };
        if (valid)
        {
            _ = new CronJob(pattern, opts);
        }
        else
        {
            Assert.Throws<CronException>(() => new CronJob(pattern, opts));
        }
    }

    [Fact]
    public void Match()
    {
        var job = new CronJob("0 0 12 * * *", new CronJobOptions { TimeZone = Tz("Europe/Stockholm") });
        Assert.True(job.IsMatch(Utc(2025, 1, 1, 11)));
        Assert.False(job.IsMatch(Utc(2025, 1, 1, 12)));
    }

    [Fact]
    public void PreviousRuns()
    {
        var job = new CronJob("0 0 12 * * *", InUtc());
        Assert.Equal(new[] { Utc(2025, 1, 10, 12), Utc(2025, 1, 9, 12), Utc(2025, 1, 8, 12) }, job.GetPreviousRuns(Utc(2025, 1, 11)).Take(3));
        var bounded = new CronJob("0 0 12 * * *", InUtc(new CronJobOptions { StartAt = Utc(2025, 1, 9), StopAt = Utc(2025, 1, 10, 6) }));
        Assert.Equal(new[] { Utc(2025, 1, 9, 12) }, bounded.GetPreviousRuns(Utc(2025, 1, 11)).Take(3));
    }

    [Fact]
    public void OneOffJob()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        var job = new CronJob(Utc(2025, 1, 2, 3, 4, 5), options: new CronJobOptions { TimeProvider = clock });
        Assert.Null(job.Cron);
        Assert.Equal(Utc(2025, 1, 2, 3, 4, 5), job.RunOnceAt);
        Assert.Equal(Utc(2025, 1, 2, 3, 4, 5), job.GetNextRun());
        Assert.Empty(job.GetNextRuns(Utc(2025, 1, 2, 3, 4, 5)).Take(2));
        Assert.True(job.IsMatch(Utc(2025, 1, 2, 3, 4, 5, 999)));
    }

    [Fact]
    public void DateStringIsNotAPattern()
    {
        // One-off jobs take a DateTimeOffset; a date string is just an invalid pattern.
        Assert.Throws<CronException>(() => new CronJob("2025-01-02T12:00:00"));
    }

    [Fact]
    public void OneOffInThePast()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        Assert.Null(new CronJob(Utc(2024, 12, 31), options: new CronJobOptions { TimeProvider = clock }).GetNextRun());
        Assert.Equal(Utc(2024, 12, 31), new CronJob(Utc(2024, 12, 31), options: new CronJobOptions { TimeProvider = clock, RunIfPast = true }).GetNextRun());
    }

    [Fact]
    public void NamedJobs()
    {
        string name = "named-" + Guid.NewGuid();
        var job = new CronJob("* * * * *", new CronJobOptions { Name = name });
        Assert.Equal(name, job.Name);
        Assert.Equal(name + ": * * * * *", job.ToString());
        using var sameName = new CronJob("* * * * *", new CronJobOptions { Name = name });
        job.Stop();
        Assert.True(job.IsStopped);
    }

    // ------------------------------------------------------------ scheduling

    [Fact]
    public void RunsOnSchedule()
    {
        var clock = new ManualTime(Utc(2025, 1, 1, 0, 0, 0, 500));
        var runs = new List<DateTimeOffset>();
        using var job = new CronJob("*/10 * * * * *", j => runs.Add(j.CurrentRun!.Value), InUtc(new CronJobOptions { TimeProvider = clock }));
        Assert.True(job.IsScheduled);
        clock.Advance(TimeSpan.FromSeconds(35));
        Assert.Equal(new[] { Utc(2025, 1, 1, 0, 0, 10), Utc(2025, 1, 1, 0, 0, 20), Utc(2025, 1, 1, 0, 0, 30) }, runs);
        Assert.Equal(Utc(2025, 1, 1, 0, 0, 30), job.PreviousRun);
    }

    [Fact]
    public void MaxRunsStopsScheduling()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        int count = 0;
        using var job = new CronJob("* * * * * *", _ => count++, new CronJobOptions { MaxRuns = 3, TimeProvider = clock });
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, count);
        Assert.Equal(0, job.RemainingRuns);
        Assert.False(job.IsScheduled);
    }

    [Fact]
    public void PauseAndResume()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        int count = 0;
        using var job = new CronJob("* * * * * *", _ => count++, new CronJobOptions { TimeProvider = clock });
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(3, count);
        Assert.True(job.Pause());
        Assert.False(job.IsScheduled);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(3, count);
        Assert.True(job.Resume());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(5, count);
    }

    [Fact]
    public void StopPreventsRuns()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        int count = 0;
        var job = new CronJob("* * * * * *", _ => count++, new CronJobOptions { TimeProvider = clock });
        clock.Advance(TimeSpan.FromSeconds(2));
        job.Stop();
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(2, count);
        Assert.False(job.Resume());
    }

    [Fact]
    public void StartAtAndStopAtBoundScheduledRuns()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        var runs = new List<DateTimeOffset>();
        using var job = new CronJob("* * * * * *", j => runs.Add(j.CurrentRun!.Value),
            new CronJobOptions { TimeProvider = clock, StartAt = Utc(2025, 1, 1, 0, 0, 5), StopAt = Utc(2025, 1, 1, 0, 0, 8) });
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(new[] { Utc(2025, 1, 1, 0, 0, 5), Utc(2025, 1, 1, 0, 0, 6), Utc(2025, 1, 1, 0, 0, 7) }, runs);
    }

    [Fact]
    public void ScheduleLater()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        int count = 0;
        using var job = new CronJob("* * * * * *", new CronJobOptions { TimeProvider = clock });
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(0, count);
        job.Start(_ => count++);
        Assert.Throws<InvalidOperationException>(() => job.Start(_ => { }));
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(3, count);
    }

    [Fact]
    public void ContextIsPassed()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        object? seen = null;
        using var job = new CronJob("* * * * * *", j => seen = j.Context, new CronJobOptions { TimeProvider = clock, Context = "ctx" });
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("ctx", seen);
    }

    [Fact]
    public void OnErrorReceivesExceptions()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        var errors = new List<Exception>();
        using var job = new CronJob("* * * * * *", _ => throw new InvalidOperationException("boom"),
            new CronJobOptions { TimeProvider = clock, OnError = (e, _) => errors.Add(e) });
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, errors.Count);
        Assert.False(job.IsExecuting);
        Assert.Equal(Utc(2025, 1, 1, 0, 0, 2), job.PreviousRun);
    }

    [Fact]
    public async Task TriggerPropagatesWithoutOnError()
    {
        var job = new CronJob("* * * * * *", new CronJobOptions());
        job.Start(_ => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => job.TriggerAsync());
        Assert.False(job.IsExecuting);
        job.Stop();
    }

    [Fact]
    public async Task PreventOverlapSkipsRuns()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        var gate = new TaskCompletionSource();
        int started = 0;
        int blocked = 0;
        using var job = new CronJob("* * * * * *", async _ =>
        {
            started++;
            await gate.Task;
        }, new CronJobOptions { TimeProvider = clock, OnSkipped = _ => System.Threading.Interlocked.Increment(ref blocked) });

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(job.IsExecuting);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(1, started);

        gate.SetResult();
        await Task.Delay(50);
        Assert.False(job.IsExecuting);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, started);

        for (int i = 0; i < 100 && System.Threading.Volatile.Read(ref blocked) < 3; i++)
        {
            await Task.Delay(10);
        }
        Assert.Equal(3, System.Threading.Volatile.Read(ref blocked));
    }

    [Fact]
    public void TimeToNext()
    {
        var job = new CronJob("0 0 12 * * *", InUtc());
        Assert.Equal(TimeSpan.FromHours(2), job.GetTimeToNextRun(Utc(2025, 1, 1, 10)));
    }

    [Fact]
    public void OneOffRunsOnce()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        int count = 0;
        using var job = new CronJob(Utc(2025, 1, 1, 0, 0, 5), _ => count++, new CronJobOptions { TimeProvider = clock });
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, count);
        Assert.False(job.IsScheduled);
    }
}

public class RealClockTests
{
    private static DateTimeOffset Utc(int y, int mo, int d) => new(y, mo, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunsOnTheSystemClock()
    {
        var fired = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var job = new CronJob("* * * * * *", j => fired.TrySetResult(j.CurrentRun!.Value), new CronJobOptions { MaxRuns = 1 });
        var done = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(fired.Task, done);
        Assert.Equal(0, job.RemainingRuns);
    }

    // ------------------------------------------------------------ stopping and cancellation

    [Fact]
    public async Task StopAsyncCancelsAndWaitsForRunningCallback()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        var started = new TaskCompletionSource();
        bool finished = false;
        var job = new CronJob("* * * * * *", async (_, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                await Task.Delay(20, CancellationToken.None);
                finished = true;
            }
        }, new CronJobOptions { TimeProvider = clock });

        clock.Advance(TimeSpan.FromSeconds(1));
        await started.Task;
        Assert.True(job.IsExecuting);

        await job.StopAsync();
        Assert.True(finished);
        Assert.False(job.IsExecuting);
        Assert.True(job.IsStopped);
    }

    [Fact]
    public async Task DisposeAsyncStopsTheJob()
    {
        var clock = new ManualTime(Utc(2025, 1, 1));
        int runs = 0;
        var job = new CronJob("* * * * * *", _ => runs++, new CronJobOptions { TimeProvider = clock });
        clock.Advance(TimeSpan.FromSeconds(1));
        await job.DisposeAsync();
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, runs);
        Assert.False(job.IsScheduled);
    }

    [Fact]
    public async Task StopAsyncCanStopWaiting()
    {
        var gate = new TaskCompletionSource();
        var job = new CronJob("* * * * * *", _ => gate.Task, new CronJobOptions { StartPaused = true });
        Task run = job.TriggerAsync();
        using var cts = new CancellationTokenSource();
        Task stopping = job.StopAsync(cts.Token);
        Assert.False(stopping.IsCompleted);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
        gate.SetResult();
        await run;
    }

    [Fact]
    public async Task TriggerAsyncPassesCancellation()
    {
        using var job = new CronJob("* * * * * *", (_, ct) => Task.Delay(Timeout.Infinite, ct), new CronJobOptions { StartPaused = true });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.TriggerAsync(cts.Token));
        Assert.False(job.IsExecuting);
    }

    [Fact]
    public async Task IsExecutingCountsOverlappingRuns()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var gates = new Queue<TaskCompletionSource>(new[] { first, second });
        using var job = new CronJob("* * * * * *", _ => gates.Dequeue().Task, new CronJobOptions { StartPaused = true });
        Task a = job.TriggerAsync();
        Task b = job.TriggerAsync();
        first.SetResult();
        await a;
        Assert.True(job.IsExecuting);
        second.SetResult();
        await b;
        Assert.False(job.IsExecuting);
    }

    [Fact]
    public void StartTwiceThrows()
    {
        using var job = new CronJob("* * * * *", new CronJobOptions());
        job.Start(_ => { });
        Assert.Throws<InvalidOperationException>(() => job.Start(_ => { }));
    }
}
