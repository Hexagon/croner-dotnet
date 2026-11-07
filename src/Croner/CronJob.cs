using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Croner;

/// <summary>
/// A scheduled job: runs a callback on a cron pattern (or once, at a given time), and answers
/// questions about its schedule.
/// </summary>
/// <example>
/// <code>
/// using var job = new CronJob("*/5 * * * * *", _ => Console.WriteLine("Every 5 seconds"));
///
/// // Only evaluate, never run:
/// var next = new CronJob("0 0 * * FRI").NextRun();
/// </code>
/// </example>
public sealed class CronJob : IDisposable
{
    private static readonly List<CronJob> Registry = new();

    // Longest single timer wait; longer waits are re-checked when it fires.
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    private readonly object _lock = new();
    private readonly Zone _zone;
    private readonly TimeProvider _time;
    private readonly DateTimeOffset? _startAt;
    private readonly DateTimeOffset? _stopAt;
    private readonly TimeSpan _startAtRounding;
    private readonly DateTimeOffset? _once;

    private Func<CronJob, Task>? _callback;
    private ITimer? _timer;
    private bool _kill;
    private bool _paused;
    private volatile bool _blocking;
    private int? _maxRuns;
    private DateTimeOffset? _currentRun;
    private DateTimeOffset? _previousRun;

    /// <summary>
    /// Creates a job without a callback, to evaluate the schedule. Call <see cref="Schedule(Action{CronJob})"/> to start it.
    /// </summary>
    /// <param name="pattern">A cron pattern, or an ISO 8601 date and time (containing <c>:</c>) for a one-off job.</param>
    /// <param name="options">Options.</param>
    public CronJob(string pattern, CronJobOptions? options = null)
        : this(pattern ?? throw new ArgumentNullException(nameof(pattern)), null, (Func<CronJob, Task>?)null, options)
    {
    }

    /// <summary>Creates and starts a job.</summary>
    /// <param name="pattern">A cron pattern, or an ISO 8601 date and time (containing <c>:</c>) for a one-off job.</param>
    /// <param name="callback">Runs on every match.</param>
    /// <param name="options">Options.</param>
    public CronJob(string pattern, Action<CronJob> callback, CronJobOptions? options = null)
        : this(pattern ?? throw new ArgumentNullException(nameof(pattern)), null,
            Wrap(callback ?? throw new ArgumentNullException(nameof(callback))), options)
    {
    }

    /// <summary>Creates and starts a job with an asynchronous callback.</summary>
    /// <param name="pattern">A cron pattern, or an ISO 8601 date and time (containing <c>:</c>) for a one-off job.</param>
    /// <param name="callback">Runs on every match. Overrun protection waits for the returned task.</param>
    /// <param name="options">Options.</param>
    public CronJob(string pattern, Func<CronJob, Task> callback, CronJobOptions? options = null)
        : this(pattern ?? throw new ArgumentNullException(nameof(pattern)), null,
            callback ?? throw new ArgumentNullException(nameof(callback)), options)
    {
    }

    /// <summary>Creates a one-off job that runs once, at <paramref name="runAt"/>. Without a callback it only evaluates.</summary>
    public CronJob(DateTimeOffset runAt, Action<CronJob>? callback = null, CronJobOptions? options = null)
        : this(null, runAt, callback is null ? null : Wrap(callback), options)
    {
    }

    /// <summary>Creates a one-off asynchronous job that runs once, at <paramref name="runAt"/>.</summary>
    public CronJob(DateTimeOffset runAt, Func<CronJob, Task> callback, CronJobOptions? options = null)
        : this(null, runAt, callback ?? throw new ArgumentNullException(nameof(callback)), options)
    {
    }

    private CronJob(string? pattern, DateTimeOffset? runAt, Func<CronJob, Task>? callback, CronJobOptions? options)
    {
        Options = options ?? new CronJobOptions();
        Name = Options.Name;
        _time = Options.TimeProvider ?? TimeProvider.System;

        if (Options.TimeZone is not null && Options.UtcOffset is not null)
        {
            throw new ArgumentException("Combining 'UtcOffset' with 'TimeZone' is not allowed.", nameof(options));
        }
        if (Options.UtcOffset is TimeSpan offset && (offset < TimeSpan.FromMinutes(-870) || offset > TimeSpan.FromMinutes(870)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "UtcOffset out of bounds.");
        }
        if (Options.Interval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Interval can not be negative.");
        }
        if (Options.MaxRuns is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRuns can not be negative.");
        }

        _zone = Options.TimeZone is not null ? Zone.Of(Options.TimeZone)
            : Options.UtcOffset is TimeSpan fixedOffset ? Zone.Fixed(fixedOffset)
            : Zone.Of(TimeZoneInfo.Local);

        if (Options.StartAt is DateTimeOffset startAt)
        {
            // Read startAt as a whole second, rounding up.
            _startAt = RoundUpToSecond(startAt);
            _startAtRounding = _startAt.Value - startAt;
        }
        if (Options.StopAt is DateTimeOffset stopAt)
        {
            _stopAt = RoundUpToSecond(stopAt);
        }

        _maxRuns = Options.MaxRuns;
        _paused = Options.Paused;

        if (runAt is not null)
        {
            _once = runAt;
        }
        else if (pattern!.IndexOf(':') > 0)
        {
            _once = ParseDate(pattern);
        }
        else
        {
            Cron = Cron.Parse(pattern, Options.ToParserOptions());
        }

        if (Name is not null)
        {
            lock (Registry)
            {
                if (Registry.Any(j => j.Name == Name))
                {
                    throw new InvalidOperationException($"Tried to initialize new named job '{Name}', but name already taken.");
                }
                Registry.Add(this);
            }
        }

        if (callback is not null)
        {
            _callback = callback;
            ScheduleNext(null);
        }
    }

    /// <summary>Named jobs that have not been stopped.</summary>
    public static IReadOnlyList<CronJob> ScheduledJobs
    {
        get
        {
            lock (Registry)
            {
                return Registry.ToArray();
            }
        }
    }

    /// <summary>The job's name, if any.</summary>
    public string? Name { get; }

    /// <summary>The options the job was created with.</summary>
    public CronJobOptions Options { get; }

    /// <summary>The parsed pattern, or <c>null</c> for a one-off job.</summary>
    public Cron? Cron { get; }

    /// <summary>The value of <see cref="CronJobOptions.Context"/>.</summary>
    public object? Context => Options.Context;

    /// <summary>The original pattern, or <c>null</c> for a one-off job.</summary>
    public string? Pattern => Cron?.Pattern.Pattern;

    /// <summary>The run time of a one-off job, or <c>null</c> for a pattern job.</summary>
    public DateTimeOffset? Once => _once;

    /// <summary>Whether the job is scheduled and waiting for a future run.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return !_paused && !_kill && _callback is not null && NextTarget(_currentRun, Now()) is not null;
            }
        }
    }

    /// <summary>Whether the job has been stopped for good.</summary>
    public bool IsStopped
    {
        get
        {
            lock (_lock)
            {
                return _kill;
            }
        }
    }

    /// <summary>Whether the callback is executing right now.</summary>
    public bool IsBusy => _blocking;

    /// <summary>The start time of the current run, or of the latest if none is in progress.</summary>
    public DateTimeOffset? CurrentRun
    {
        get
        {
            lock (_lock)
            {
                return _currentRun;
            }
        }
    }

    /// <summary>The start time of the latest completed run.</summary>
    public DateTimeOffset? PreviousRun
    {
        get
        {
            lock (_lock)
            {
                return _previousRun;
            }
        }
    }

    /// <summary>Remaining scheduled runs, or <c>null</c> for unlimited.</summary>
    public int? RunsLeft
    {
        get
        {
            lock (_lock)
            {
                return _maxRuns;
            }
        }
    }

    /// <summary>Finds the next run after <paramref name="from"/> (default: now), or <c>null</c> if there is none.</summary>
    public DateTimeOffset? NextRun(DateTimeOffset? from = null)
    {
        lock (_lock)
        {
            return NextTarget(from, Now());
        }
    }

    /// <summary>Finds the next <paramref name="count"/> runs after <paramref name="from"/> (default: the current run, or now).</summary>
    public IReadOnlyList<DateTimeOffset> NextRuns(int count, DateTimeOffset? from = null)
    {
        lock (_lock)
        {
            if (_maxRuns is int max && count > max)
            {
                count = max;
            }
            var runs = new List<DateTimeOffset>();
            DateTimeOffset now = Now();
            DateTimeOffset? cursor = from ?? _currentRun;
            while (count-- > 0 && NextTarget(cursor, now) is DateTimeOffset run)
            {
                runs.Add(run);
                cursor = run;
            }
            return runs;
        }
    }

    /// <summary>Finds the previous <paramref name="count"/> runs before <paramref name="reference"/> (default: now), most recent first.</summary>
    public IReadOnlyList<DateTimeOffset> PreviousRuns(int count, DateTimeOffset? reference = null)
    {
        lock (_lock)
        {
            var runs = new List<DateTimeOffset>();
            DateTimeOffset now = Now();
            DateTimeOffset? cursor = reference;
            while (count-- > 0 && PreviousTarget(cursor, now) is DateTimeOffset run)
            {
                runs.Add(run);
                cursor = run;
            }
            return runs;
        }
    }

    /// <summary>
    /// Enumerates future runs after <paramref name="startAt"/> (exclusive, default now), respecting
    /// <see cref="CronJobOptions.MaxRuns"/>, <see cref="CronJobOptions.StopAt"/> and the other options.
    /// </summary>
    public IEnumerable<DateTimeOffset> Enumerate(DateTimeOffset? startAt = null)
    {
        int remaining;
        DateTimeOffset now;
        lock (_lock)
        {
            remaining = _maxRuns ?? int.MaxValue;
            now = Now();
        }
        DateTimeOffset? cursor = startAt;
        while (remaining-- > 0)
        {
            DateTimeOffset? next;
            lock (_lock)
            {
                next = NextTarget(cursor, now);
            }
            if (next is null)
            {
                yield break;
            }
            yield return next.Value;
            cursor = next;
        }
    }

    /// <summary>Time from <paramref name="from"/> (default now) to the next run, or <c>null</c> if there is none.</summary>
    public TimeSpan? TimeToNext(DateTimeOffset? from = null)
    {
        lock (_lock)
        {
            DateTimeOffset now = Now();
            return NextTarget(from, now) is DateTimeOffset next ? next - (from ?? now) : null;
        }
    }

    /// <summary>Checks whether <paramref name="time"/> (to the second) matches the schedule.</summary>
    public bool Match(DateTimeOffset time)
    {
        if (_once is DateTimeOffset once)
        {
            return TruncateToSecond(time) == TruncateToSecond(once);
        }
        return Cron!.IsTimeMatching(_zone.ToZone(time));
    }

    /// <summary>Pauses the job. Returns <c>false</c> if it was stopped.</summary>
    public bool Pause()
    {
        lock (_lock)
        {
            _paused = true;
            return !_kill;
        }
    }

    /// <summary>Resumes a paused job. Returns <c>false</c> if it was stopped.</summary>
    public bool Resume()
    {
        lock (_lock)
        {
            _paused = false;
            return !_kill;
        }
    }

    /// <summary>
    /// Stops the job for good. A run in progress finishes. A named job is removed from <see cref="ScheduledJobs"/>.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _kill = true;
            _timer?.Dispose();
            _timer = null;
        }
        lock (Registry)
        {
            Registry.Remove(this);
        }
    }

    /// <summary>Stops the job.</summary>
    public void Dispose() => Stop();

    /// <summary>Starts a job created without a callback.</summary>
    public CronJob Schedule(Action<CronJob> callback) =>
        Schedule(Wrap(callback ?? throw new ArgumentNullException(nameof(callback))));

    /// <summary>Starts a job created without a callback, with an asynchronous callback.</summary>
    public CronJob Schedule(Func<CronJob, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_lock)
        {
            if (_callback is not null)
            {
                throw new InvalidOperationException("It is not allowed to schedule two functions using the same CronJob instance.");
            }
            _callback = callback;
        }
        ScheduleNext(null);
        return this;
    }

    /// <summary>
    /// Runs the callback now, outside the schedule. Does not count against <see cref="CronJobOptions.MaxRuns"/>.
    /// Exceptions propagate unless <see cref="CronJobOptions.Catch"/> or <see cref="CronJobOptions.OnError"/> is set.
    /// </summary>
    public Task Trigger() => Run(Now());

    // ------------------------------------------------------------------
    // Scheduling
    // ------------------------------------------------------------------

    private static Func<CronJob, Task> Wrap(Action<CronJob> callback) => job =>
    {
        callback(job);
        return Task.CompletedTask;
    };

    private DateTimeOffset Now() => _zone.ToZone(_time.GetUtcNow());

    private bool CatchErrors => Options.Catch || Options.OnError is not null;

    private bool ProtectRuns => Options.Protect || Options.OnBlocked is not null;

    private void ScheduleNext(DateTimeOffset? nowOverride)
    {
        lock (_lock)
        {
            if (_kill || _callback is null)
            {
                return;
            }

            DateTimeOffset now = nowOverride ?? Now();
            if (NextTarget(null, now) is not DateTimeOffset next)
            {
                return;
            }
            TimeSpan wait = next - now;

            // If the clock stepped backward past the current run, search from now so that target and wait agree.
            DateTimeOffset? previous = _currentRun;
            if (previous > now)
            {
                previous = null;
            }
            if (NextTarget(previous, now) is not DateTimeOffset target)
            {
                return;
            }

            if (wait > MaxDelay)
            {
                wait = MaxDelay;
            }
            if (wait < TimeSpan.Zero)
            {
                wait = _paused ? TimeSpan.FromSeconds(1) : TimeSpan.Zero;
            }

            _timer?.Dispose();
            _timer = _time.CreateTimer(_ => CheckTrigger(target), null, wait, Timeout.InfiniteTimeSpan);
        }
    }

    private void CheckTrigger(DateTimeOffset target)
    {
        DateTimeOffset now = Now();
        bool shouldRun, isBlocked;
        lock (_lock)
        {
            if (_kill)
            {
                return;
            }
            shouldRun = !_paused && now >= target;
            isBlocked = _blocking && ProtectRuns;
            if (shouldRun && !isBlocked && _maxRuns is not null)
            {
                _maxRuns--;
            }
        }

        if (shouldRun && !isBlocked)
        {
            _ = RunScheduled(now);
        }
        else if (shouldRun && isBlocked && Options.OnBlocked is Action<CronJob> onBlocked)
        {
            ThreadPool.QueueUserWorkItem(_ => onBlocked(this));
        }

        ScheduleNext(shouldRun ? null : now);
    }

    private async Task RunScheduled(DateTimeOffset initiation)
    {
        try
        {
            await Run(initiation).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Not caught by the job: surface it like an unhandled exception in a timer callback.
            ExceptionDispatchInfo info = ExceptionDispatchInfo.Capture(ex);
            ThreadPool.QueueUserWorkItem(_ => info.Throw());
        }
    }

    private async Task Run(DateTimeOffset initiation)
    {
        Func<CronJob, Task>? callback;
        lock (_lock)
        {
            _blocking = true;
            _currentRun = initiation;
            callback = _callback;
        }

        try
        {
            if (callback is not null)
            {
                if (CatchErrors)
                {
                    try
                    {
                        await callback(this).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            Options.OnError?.Invoke(ex, this);
                        }
                        catch
                        {
                            // Errors thrown by the error handler must not disrupt the job.
                        }
                    }
                }
                else
                {
                    await callback(this).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _previousRun = initiation;
                _blocking = false;
            }
        }
    }

    // ------------------------------------------------------------------
    // Schedule calculations (call with _lock held)
    // ------------------------------------------------------------------

    private TimeSpan DayOffset => TimeSpan.FromDays(Options.DayOffset);

    private DateTimeOffset? Shift(DateTimeOffset? date) => date is DateTimeOffset d ? _zone.ToZone(d - DayOffset) : null;

    private DateTimeOffset? Unshift(DateTimeOffset? date) => date is DateTimeOffset d ? _zone.ToZone(d + DayOffset) : null;

    /// <summary>
    /// The next run after <paramref name="reference"/> (default now). With a day offset, the pattern is searched in
    /// shifted time, so the reference, startAt and stopAt are all compared with the actual run time.
    /// </summary>
    private DateTimeOffset? NextTarget(DateTimeOffset? reference, DateTimeOffset now) =>
        Unshift(Next(Shift(reference), Shift(now)!.Value, Shift(_startAt), Shift(_stopAt)));

    private DateTimeOffset? PreviousTarget(DateTimeOffset? reference, DateTimeOffset now) =>
        Unshift(Previous(Shift(reference ?? now)!.Value, Shift(_startAt), Shift(_stopAt)));

    private DateTimeOffset? Next(DateTimeOffset? previousRun, DateTimeOffset now, DateTimeOffset? startAt, DateTimeOffset? stopAt)
    {
        bool isEnumerating = previousRun is not null;
        bool hasPreviousRun = previousRun is not null || _currentRun is not null;

        // With startAt and an interval but no previous run, work out when the last run should have been.
        bool startAtInFutureWithInterval = false;
        if (previousRun is null && startAt is not null && Options.Interval > TimeSpan.Zero)
        {
            previousRun = CalculatePreviousRun(startAt.Value, now);
            if (previousRun is not null)
            {
                hasPreviousRun = true;
            }
            startAtInFutureWithInterval = previousRun is null;
        }

        DateTimeOffset reference = previousRun ?? now;

        // The search never starts before startAt.
        if (startAt is not null && reference < startAt)
        {
            reference = startAtInFutureWithInterval ? startAt.Value : startAt.Value.AddSeconds(-1);
        }

        DateTimeOffset? nextRun;
        if (_once is DateTimeOffset once)
        {
            nextRun = once;
            long past = (reference - once).Ticks;
            if ((startAt is not null && once < startAt - _startAtRounding)
                || (_currentRun is not null && once + DayOffset <= _currentRun)
                || (isEnumerating ? past >= 0 : past > TimeSpan.TicksPerSecond && !Options.AllowPast))
            {
                return null;
            }
        }
        else if (startAtInFutureWithInterval)
        {
            nextRun = startAt;
        }
        else
        {
            nextRun = FindNext(reference, hasPreviousRun);
        }

        if (nextRun is null || _maxRuns is <= 0 || _kill || (stopAt is not null && nextRun >= stopAt))
        {
            return null;
        }
        return nextRun;
    }

    private DateTimeOffset? Previous(DateTimeOffset reference, DateTimeOffset? startAt, DateTimeOffset? stopAt)
    {
        if (stopAt is not null && reference > stopAt)
        {
            reference = stopAt.Value;
        }

        if (_once is DateTimeOffset once)
        {
            return once < reference ? once : null;
        }

        DateTimeOffset? previous;
        try
        {
            previous = Options.Interval > TimeSpan.FromSeconds(1)
                ? Cron!.FindOccurrence(TruncateToSecond(reference) - Options.Interval, _zone, true, Direction.Backward)
                : Cron!.FindOccurrence(reference, _zone, false, Direction.Backward);
        }
        catch (CronException)
        {
            return null;
        }

        return previous is null || (startAt is not null && previous < startAt) ? null : previous;
    }

    /// <summary>The first match after <paramref name="reference"/>, at least one interval after a previous run.</summary>
    private DateTimeOffset? FindNext(DateTimeOffset reference, bool hasPreviousRun)
    {
        try
        {
            return hasPreviousRun && Options.Interval > TimeSpan.FromSeconds(1)
                ? Cron!.FindOccurrence(TruncateToSecond(reference) + Options.Interval, _zone, true, Direction.Forward)
                : Cron!.FindOccurrence(reference, _zone, false, Direction.Forward);
        }
        catch (CronException)
        {
            return null;
        }
    }

    /// <summary>
    /// With startAt and an interval and no previous run: the last run there would have been between startAt and now,
    /// or <c>null</c> when startAt is in the future.
    /// </summary>
    private DateTimeOffset? CalculatePreviousRun(DateTimeOffset startAt, DateTimeOffset now)
    {
        if (startAt > now)
        {
            return null;
        }
        DateTimeOffset previous = startAt;
        while (previous + Options.Interval <= now)
        {
            if (FindNext(previous, true) is not DateTimeOffset next)
            {
                break;
            }
            previous = next;
        }
        return previous;
    }

    private static DateTimeOffset TruncateToSecond(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    private DateTimeOffset RoundUpToSecond(DateTimeOffset value)
    {
        long rest = value.Ticks % TimeSpan.TicksPerSecond;
        return _zone.ToZone(rest == 0 ? value : value.AddTicks(TimeSpan.TicksPerSecond - rest));
    }

    private DateTimeOffset ParseDate(string value)
    {
        DateTime parsed = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (parsed.Kind != DateTimeKind.Unspecified)
        {
            // Carries its own offset ("Z" or "+02:00").
            return _zone.ToZone(new DateTimeOffset(parsed.ToUniversalTime(), TimeSpan.Zero));
        }
        // A wall clock time in the job's time zone.
        Resolution resolution = _zone.Resolve(parsed);
        if (resolution.Kind == ResolutionKind.Gap)
        {
            throw new ArgumentException($"'{value}' does not exist in the job's time zone.", "pattern");
        }
        return resolution.Earlier;
    }
}
