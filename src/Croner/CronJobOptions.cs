using System;

namespace Croner;

/// <summary>Options for a <see cref="CronJob"/>.</summary>
public sealed record CronJobOptions
{
    /// <summary>A name for the job, e.g. for logging. Available as <see cref="CronJob.Name"/>.</summary>
    public string? Name { get; init; }

    /// <summary>How the pattern is parsed. Default <see cref="CronParserOptions.Default"/>.</summary>
    public CronParserOptions? Parser { get; init; }

    /// <summary>
    /// The time zone the pattern is evaluated in. Default: the <see cref="TimeProvider"/>'s local time zone,
    /// which is <see cref="TimeZoneInfo.Local"/> for the system clock. Cannot be combined with <see cref="UtcOffset"/>.
    /// </summary>
    public TimeZoneInfo? TimeZone { get; init; }

    /// <summary>
    /// Evaluate the pattern at a fixed UTC offset instead of a time zone (no DST).
    /// Must be between -14:30 and +14:30. Cannot be combined with <see cref="TimeZone"/>.
    /// </summary>
    public TimeSpan? UtcOffset { get; init; }

    /// <summary>The earliest time the job may run (inclusive).</summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>The time the job stops running (exclusive).</summary>
    public DateTimeOffset? StopAt { get; init; }

    /// <summary>Maximum number of scheduled runs. <c>null</c> (default) means unlimited.</summary>
    public int? MaxRuns { get; init; }

    /// <summary>Minimum time between two runs, in whole seconds. Default <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan MinimumInterval { get; init; }

    /// <summary>
    /// Run this many days after (positive) or before (negative) each match of the pattern.
    /// For example <c>0 0 * * 5L</c> with <c>DayOffset = -1</c> runs the day before the last Friday of each month.
    /// </summary>
    public int DayOffset { get; init; }

    /// <summary>Create the job paused. Default <c>false</c>.</summary>
    public bool StartPaused { get; init; }

    /// <summary>Skip a run while the previous one is still executing. Default <c>false</c>.</summary>
    public bool PreventOverlap { get; init; }

    /// <summary>Called when a run is skipped because the previous one is still executing. Setting it implies <see cref="PreventOverlap"/>.</summary>
    public Action<CronJob>? OnSkipped { get; init; }

    /// <summary>
    /// Called with exceptions thrown by the callback. When it is not set, an exception from a scheduled run is
    /// rethrown on the thread pool, which ends the process like an unhandled exception in any timer callback,
    /// and an exception from <see cref="CronJob.TriggerAsync"/> goes to the returned task.
    /// </summary>
    public Action<Exception, CronJob>? OnError { get; init; }

    /// <summary>For one-off jobs: run right away when the time has already passed, instead of never.</summary>
    public bool RunIfPast { get; init; }

    /// <summary>Any value; available to the callback through <see cref="CronJob.Context"/>.</summary>
    public object? Context { get; init; }

    /// <summary>The clock and timers to use. Default <see cref="System.TimeProvider.System"/>. Useful for tests.</summary>
    public TimeProvider? TimeProvider { get; init; }
}
