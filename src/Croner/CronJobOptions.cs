using System;

namespace Croner;

/// <summary>Which pattern lengths a <see cref="CronJob"/> accepts.</summary>
public enum CronMode
{
    /// <summary>5, 6 or 7 fields (seconds and year optional).</summary>
    Auto,

    /// <summary>Exactly 5 fields: minute precision, no seconds or year.</summary>
    FivePart,

    /// <summary>Exactly 6 fields: seconds required, no year.</summary>
    SixPart,

    /// <summary>Exactly 7 fields: seconds and year required.</summary>
    SevenPart,

    /// <summary>5 or 6 fields: seconds optional, no year.</summary>
    FiveOrSixParts,

    /// <summary>6 or 7 fields: seconds required, year optional.</summary>
    SixOrSevenParts,
}

/// <summary>Options for a <see cref="CronJob"/>.</summary>
public sealed record CronJobOptions
{
    /// <summary>
    /// A unique name for the job. Named jobs are listed in <see cref="CronJob.ScheduledJobs"/>
    /// until stopped; creating a second job with the same name throws.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>Start the job paused. Default <c>false</c>.</summary>
    public bool Paused { get; init; }

    /// <summary>
    /// Catch exceptions thrown by the callback. When <c>false</c> (default) and <see cref="OnError"/> is not set,
    /// an exception from a scheduled run is rethrown on the thread pool, which terminates the process just like an
    /// unhandled exception in any timer callback.
    /// </summary>
    public bool Catch { get; init; }

    /// <summary>Called with exceptions thrown by the callback. Setting it implies <see cref="Catch"/>.</summary>
    public Action<Exception, CronJob>? OnError { get; init; }

    /// <summary>Maximum number of scheduled runs. <c>null</c> (default) means unlimited.</summary>
    public int? MaxRuns { get; init; }

    /// <summary>Minimum time between two runs, in whole seconds. Default <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan Interval { get; init; }

    /// <summary>
    /// Overrun protection: skip a run while the previous one is still in progress. Default <c>false</c>.
    /// </summary>
    public bool Protect { get; init; }

    /// <summary>Called when a run is skipped by overrun protection. Setting it implies <see cref="Protect"/>.</summary>
    public Action<CronJob>? OnBlocked { get; init; }

    /// <summary>The earliest time the job may run (inclusive).</summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>The time the job stops running (exclusive).</summary>
    public DateTimeOffset? StopAt { get; init; }

    /// <summary>
    /// The time zone the pattern is evaluated in. Default <see cref="TimeZoneInfo.Local"/>.
    /// Cannot be combined with <see cref="UtcOffset"/>.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; init; }

    /// <summary>
    /// Evaluate the pattern at a fixed UTC offset instead of a time zone (no DST).
    /// Must be between -14:30 and +14:30. Cannot be combined with <see cref="TimeZone"/>.
    /// </summary>
    public TimeSpan? UtcOffset { get; init; }

    /// <summary>Require both the day-of-month and day-of-week fields to match (AND). Default <c>false</c> (OR).</summary>
    public bool DomAndDow { get; init; }

    /// <summary>
    /// Run this many days after (positive) or before (negative) each match of the pattern.
    /// For example <c>0 0 * * 5L</c> with <c>DayOffset = -1</c> runs the day before the last Friday of each month.
    /// </summary>
    public int DayOffset { get; init; }

    /// <summary>Which pattern lengths are accepted. Default <see cref="CronMode.Auto"/>.</summary>
    public CronMode Mode { get; init; } = CronMode.Auto;

    /// <summary>Use Quartz-style weekday numbering (1 = Sunday … 7 = Saturday).</summary>
    public bool AlternativeWeekdays { get; init; }

    /// <summary>Accept the non-standard step syntax <c>0/10</c> and <c>/10</c>.</summary>
    public bool SloppyRanges { get; init; }

    /// <summary>
    /// For one-off jobs: run immediately when the date is already in the past, instead of never.
    /// </summary>
    public bool AllowPast { get; init; }

    /// <summary>Any value; available to the callback through <see cref="CronJob.Context"/>.</summary>
    public object? Context { get; init; }

    /// <summary>The clock and timers to use. Default <see cref="System.TimeProvider.System"/>. Useful for tests.</summary>
    public TimeProvider? TimeProvider { get; init; }

    internal CronParserOptions ToParserOptions()
    {
        (SecondsMode seconds, YearMode year) = Mode switch
        {
            CronMode.FivePart => (SecondsMode.Disallowed, YearMode.Disallowed),
            CronMode.SixPart => (SecondsMode.Required, YearMode.Disallowed),
            CronMode.SevenPart => (SecondsMode.Required, YearMode.Required),
            CronMode.FiveOrSixParts => (SecondsMode.Optional, YearMode.Disallowed),
            CronMode.SixOrSevenParts => (SecondsMode.Required, YearMode.Optional),
            _ => (SecondsMode.Optional, YearMode.Optional),
        };
        return new CronParserOptions
        {
            Seconds = seconds,
            Year = year,
            DomAndDow = DomAndDow,
            AlternativeWeekdays = AlternativeWeekdays,
            SloppyRanges = SloppyRanges,
        };
    }
}
