using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Croner;

/// <summary>
/// A scheduled job: runs a callback on a cron pattern (or once, at a given time), and answers
/// questions about its schedule.
/// </summary>
/// <remarks>
/// Members are thread-safe. Dispose the job, or call <see cref="Stop"/> or <see cref="StopAsync"/>,
/// to stop its timer.
/// </remarks>
/// <example>
/// <code>
/// using var job = new CronJob("*/5 * * * * *", _ => Console.WriteLine("Every 5 seconds"));
///
/// // Only evaluate, never run:
/// DateTimeOffset? next = new CronJob("0 0 * * FRI").GetNextRun();
/// </code>
/// </example>
public sealed class CronJob : IDisposable, IAsyncDisposable
{
    // Longest single timer wait; longer waits are re-checked when it fires.
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    private readonly object _lock = new();
    private readonly Zone _zone;
    private readonly TimeProvider _time;
    private readonly DateTimeOffset? _startAt;
    private readonly DateTimeOffset? _stopAt;
    private readonly TimeSpan _startAtRounding;
    private readonly DateTimeOffset? _once;
    private readonly CancellationTokenSource _stopping = new();
    private readonly HashSet<Task> _inFlight = new();

    private Func<CronJob, CancellationToken, Task>? _callback;
    private ITimer? _timer;
    private bool _kill;
    private bool _paused;
    private int _executing;
    private int? _maxRuns;
    private DateTimeOffset? _currentRun;
    private DateTimeOffset? _previousRun;

    /// <summary>
    /// Creates a job that only evaluates its schedule. Call <see cref="Start(Action{CronJob})"/> to run a callback.
    /// </summary>
    /// <param name="pattern">A cron pattern.</param>
    /// <param name="options">Options.</param>
    /// <exception cref="CronException">The pattern is invalid.</exception>
    public CronJob(string pattern, CronJobOptions? options = null)
        : this(ParsePattern(pattern, options), null, null, options)
    {
    }

    /// <summary>Creates and starts a job.</summary>
    /// <param name="pattern">A cron pattern.</param>
    /// <param name="callback">Runs on every match.</param>
    /// <param name="options">Options.</param>
    /// <exception cref="CronException">The pattern is invalid.</exception>
    public CronJob(string pattern, Action<CronJob> callback, CronJobOptions? options = null)
        : this(ParsePattern(pattern, options), null, Wrap(callback), options)
    {
    }

    /// <summary>Creates and starts a job with an asynchronous callback.</summary>
    /// <param name="pattern">A cron pattern.</param>
    /// <param name="callback">Runs on every match. Overlap prevention waits for the returned task.</param>
    /// <param name="options">Options.</param>
    /// <exception cref="CronException">The pattern is invalid.</exception>
    public CronJob(string pattern, Func<CronJob, Task> callback, CronJobOptions? options = null)
        : this(ParsePattern(pattern, options), null, Wrap(callback), options)
    {
    }

    /// <summary>Creates and starts a job with an asynchronous, cancellable callback.</summary>
    /// <param name="pattern">A cron pattern.</param>
    /// <param name="callback">
    /// Runs on every match. The token is cancelled when the job is stopped or disposed.
    /// Overlap prevention waits for the returned task.
    /// </param>
    /// <param name="options">Options.</param>
    /// <exception cref="CronException">The pattern is invalid.</exception>
    public CronJob(string pattern, Func<CronJob, CancellationToken, Task> callback, CronJobOptions? options = null)
        : this(ParsePattern(pattern, options), null, callback ?? throw new ArgumentNullException(nameof(callback)), options)
    {
    }

    /// <summary>Creates a one-off job that only evaluates its schedule.</summary>
    /// <param name="runAt">When the job runs.</param>
    /// <param name="options">Options.</param>
    public CronJob(DateTimeOffset runAt, CronJobOptions? options = null)
        : this(null, runAt, null, options)
    {
    }

    /// <summary>Creates and starts a job that runs once, at <paramref name="runAt"/>.</summary>
    public CronJob(DateTimeOffset runAt, Action<CronJob> callback, CronJobOptions? options = null)
        : this(null, runAt, Wrap(callback), options)
    {
    }

    /// <summary>Creates and starts an asynchronous job that runs once, at <paramref name="runAt"/>.</summary>
    public CronJob(DateTimeOffset runAt, Func<CronJob, Task> callback, CronJobOptions? options = null)
        : this(null, runAt, Wrap(callback), options)
    {
    }

    /// <summary>Creates and starts an asynchronous, cancellable job that runs once, at <paramref name="runAt"/>.</summary>
    public CronJob(DateTimeOffset runAt, Func<CronJob, CancellationToken, Task> callback, CronJobOptions? options = null)
        : this(null, runAt, callback ?? throw new ArgumentNullException(nameof(callback)), options)
    {
    }

    private CronJob(Cron? cron, DateTimeOffset? runAt, Func<CronJob, CancellationToken, Task>? callback, CronJobOptions? options)
    {
        Options = options ?? new CronJobOptions();
        _time = Options.TimeProvider ?? TimeProvider.System;

        if (Options.TimeZone is not null && Options.UtcOffset is not null)
        {
            throw new ArgumentException("Combining 'UtcOffset' with 'TimeZone' is not allowed.", nameof(options));
        }
        if (Options.UtcOffset is TimeSpan offset && (offset < TimeSpan.FromMinutes(-870) || offset > TimeSpan.FromMinutes(870)))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "UtcOffset must be between -14:30 and +14:30.");
        }
        if (Options.MinimumInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumInterval can not be negative.");
        }
        if (Options.MaxRuns is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRuns can not be negative.");
        }

        _zone = Options.TimeZone is not null ? Zone.Of(Options.TimeZone)
            : Options.UtcOffset is TimeSpan fixedOffset ? Zone.Fixed(fixedOffset)
            : Zone.Of(_time.LocalTimeZone);

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
        _paused = Options.StartPaused;
        Cron = cron;
        _once = runAt is DateTimeOffset once ? _zone.ToZone(once) : null;

        if (callback is not null)
        {
            _callback = callback;
            ScheduleNext(null);
        }
    }

    /// <summary>The job's name, from <see cref="CronJobOptions.Name"/>.</summary>
    public string? Name => Options.Name;

    /// <summary>The options the job was created with.</summary>
    public CronJobOptions Options { get; }

    /// <summary>The parsed schedule, or <c>null</c> for a one-off job.</summary>
    public Cron? Cron { get; }

    /// <summary>The run time of a one-off job, or <c>null</c> for a cron job.</summary>
    public DateTimeOffset? RunOnceAt => _once;

    /// <summary>The value of <see cref="CronJobOptions.Context"/>.</summary>
    public object? Context => Options.Context;

    /// <summary>Whether the job has a callback, is not paused or stopped, and has a future run.</summary>
    public bool IsScheduled
    {
        get
        {
            lock (_lock)
            {
                return !_paused && !_kill && _callback is not null && NextTarget(_currentRun, Now()) is not null;
            }
        }
    }

    /// <summary>Whether the job is paused.</summary>
    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return _paused;
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
    public bool IsExecuting
    {
        get
        {
            lock (_lock)
            {
                return _executing > 0;
            }
        }
    }

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

    /// <summary>Remaining scheduled runs with <see cref="CronJobOptions.MaxRuns"/>, otherwise <c>null</c>.</summary>
    public int? RemainingRuns
    {
        get
        {
            lock (_lock)
            {
                return _maxRuns;
            }
        }
    }

    /// <summary>Gets the next run after <paramref name="from"/> (default: now), or <c>null</c> if there is none.</summary>
    public DateTimeOffset? GetNextRun(DateTimeOffset? from = null)
    {
        lock (_lock)
        {
            return NextTarget(from, Now());
        }
    }

    /// <summary>
    /// Lazily enumerates the runs after <paramref name="from"/> (default: the current run, or now), respecting
    /// <see cref="CronJobOptions.MaxRuns"/>, <see cref="CronJobOptions.StopAt"/> and the other options.
    /// </summary>
    public IEnumerable<DateTimeOffset> GetNextRuns(DateTimeOffset? from = null)
    {
        int remaining;
        DateTimeOffset now;
        DateTimeOffset? cursor;
        lock (_lock)
        {
            remaining = _maxRuns ?? int.MaxValue;
            now = Now();
            cursor = from ?? _currentRun;
        }
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

    /// <summary>
    /// Lazily enumerates the runs before <paramref name="before"/> (default: now), most recent first,
    /// respecting <see cref="CronJobOptions.StartAt"/> and the other options.
    /// </summary>
    public IEnumerable<DateTimeOffset> GetPreviousRuns(DateTimeOffset? before = null)
    {
        DateTimeOffset now;
        lock (_lock)
        {
            now = Now();
        }
        DateTimeOffset? cursor = before;
        while (true)
        {
            DateTimeOffset? previous;
            lock (_lock)
            {
                previous = PreviousTarget(cursor, now);
            }
            if (previous is null)
            {
                yield break;
            }
            yield return previous.Value;
            cursor = previous;
        }
    }

    /// <summary>Time from <paramref name="from"/> (default now) to the next run, or <c>null</c> if there is none.</summary>
    public TimeSpan? GetTimeToNextRun(DateTimeOffset? from = null)
    {
        lock (_lock)
        {
            DateTimeOffset now = Now();
            return NextTarget(from, now) is DateTimeOffset next ? next - (from ?? now) : null;
        }
    }

    /// <summary>Checks whether <paramref name="time"/> (to the second) matches the schedule.</summary>
    public bool IsMatch(DateTimeOffset time)
    {
        if (_once is DateTimeOffset once)
        {
            return TruncateToSecond(time) == TruncateToSecond(once);
        }
        return Cron!.IsMatch(_zone.ToZone(time));
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

    /// <summary>Starts a job created without a callback.</summary>
    /// <exception cref="InvalidOperationException">The job already has a callback.</exception>
    public CronJob Start(Action<CronJob> callback) => Start(Wrap(callback));

    /// <summary>Starts a job created without a callback, with an asynchronous callback.</summary>
    /// <exception cref="InvalidOperationException">The job already has a callback.</exception>
    public CronJob Start(Func<CronJob, Task> callback) => Start(Wrap(callback));

    /// <summary>
    /// Starts a job created without a callback, with an asynchronous callback whose token is cancelled when the job stops.
    /// </summary>
    /// <exception cref="InvalidOperationException">The job already has a callback.</exception>
    public CronJob Start(Func<CronJob, CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_lock)
        {
            if (_callback is not null)
            {
                throw new InvalidOperationException("The job already has a callback.");
            }
            _callback = callback;
        }
        ScheduleNext(null);
        return this;
    }

    /// <summary>
    /// Runs the callback now, outside the schedule. Does not count against <see cref="CronJobOptions.MaxRuns"/>.
    /// Exceptions from the callback go to <see cref="CronJobOptions.OnError"/> when it is set, and otherwise
    /// to the returned task.
    /// </summary>
    /// <param name="cancellationToken">Cancels the token passed to the callback, together with stopping the job.</param>
    public Task TriggerAsync(CancellationToken cancellationToken = default) => TrackRun(Now(), cancellationToken);

    /// <summary>
    /// Stops the job for good. Running callbacks see their cancellation token cancelled, but are not waited for;
    /// use <see cref="StopAsync"/> to wait.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _kill = true;
            _timer?.Dispose();
            _timer = null;
        }
        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }
    }

    /// <summary>Stops the job for good, and waits until running callbacks have finished.</summary>
    /// <param name="cancellationToken">Stops waiting; the job is stopped either way.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Stop();
        Task[] running;
        lock (_lock)
        {
            running = _inFlight.ToArray();
        }
        try
        {
            await Task.WhenAll(running).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A failed run has already been reported, to OnError, the thread pool or TriggerAsync's caller.
        }
    }

    /// <summary>Stops the job. See <see cref="Stop"/>.</summary>
    public void Dispose() => Stop();

    /// <summary>Stops the job and waits for running callbacks. See <see cref="StopAsync"/>.</summary>
    public ValueTask DisposeAsync() => new(StopAsync());

    /// <inheritdoc />
    public override string ToString() =>
        (Name is null ? "" : Name + ": ") + (Cron?.ToString() ?? _once?.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

    // ------------------------------------------------------------------
    // Scheduling
    // ------------------------------------------------------------------

    private static Cron ParsePattern(string pattern, CronJobOptions? options)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return Cron.Parse(pattern, options?.Parser);
    }

    private static Func<CronJob, CancellationToken, Task> Wrap(Action<CronJob> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return (job, _) =>
        {
            callback(job);
            return Task.CompletedTask;
        };
    }

    private static Func<CronJob, CancellationToken, Task> Wrap(Func<CronJob, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return (job, _) => callback(job);
    }

    private DateTimeOffset Now() => _zone.ToZone(_time.GetUtcNow());

    private bool PreventOverlap => Options.PreventOverlap || Options.OnSkipped is not null;

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
            isBlocked = _executing > 0 && PreventOverlap;
            if (shouldRun && !isBlocked && _maxRuns is not null)
            {
                _maxRuns--;
            }
        }

        if (shouldRun && !isBlocked)
        {
            _ = RunScheduled(now);
        }
        else if (shouldRun && isBlocked && Options.OnSkipped is Action<CronJob> onSkipped)
        {
            ThreadPool.QueueUserWorkItem(_ => onSkipped(this));
        }

        ScheduleNext(shouldRun ? null : now);
    }

    private async Task RunScheduled(DateTimeOffset initiation)
    {
        try
        {
            await TrackRun(initiation, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Not handled by OnError: surface it like an unhandled exception in a timer callback.
            ExceptionDispatchInfo info = ExceptionDispatchInfo.Capture(ex);
            ThreadPool.QueueUserWorkItem(_ => info.Throw());
        }
    }

    private Task TrackRun(DateTimeOffset initiation, CancellationToken cancellationToken)
    {
        Task run = Run(initiation, cancellationToken);
        lock (_lock)
        {
            _inFlight.Add(run);
        }
        run.ContinueWith(
            completed =>
            {
                lock (_lock)
                {
                    _inFlight.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return run;
    }

    private async Task Run(DateTimeOffset initiation, CancellationToken cancellationToken)
    {
        Func<CronJob, CancellationToken, Task>? callback;
        lock (_lock)
        {
            _executing++;
            _currentRun = initiation;
            callback = _callback;
        }

        using CancellationTokenSource? linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, cancellationToken)
            : null;
        CancellationToken token = linked?.Token ?? _stopping.Token;

        try
        {
            if (callback is not null)
            {
                try
                {
                    await callback(this, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
                {
                    // The job was stopped while the callback ran.
                }
                catch (Exception ex) when (Options.OnError is not null)
                {
                    try
                    {
                        Options.OnError(ex, this);
                    }
                    catch
                    {
                        // Errors thrown by the error handler must not disrupt the job.
                    }
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _previousRun = initiation;
                _executing--;
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
        if (previousRun is null && startAt is not null && Options.MinimumInterval > TimeSpan.Zero)
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
                || (isEnumerating ? past >= 0 : past > TimeSpan.TicksPerSecond && !Options.RunIfPast))
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
            previous = Options.MinimumInterval > TimeSpan.FromSeconds(1)
                ? Cron!.FindOccurrence(TruncateToSecond(reference) - Options.MinimumInterval, _zone, true, Direction.Backward)
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
            return hasPreviousRun && Options.MinimumInterval > TimeSpan.FromSeconds(1)
                ? Cron!.FindOccurrence(TruncateToSecond(reference) + Options.MinimumInterval, _zone, true, Direction.Forward)
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
        while (previous + Options.MinimumInterval <= now)
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
}
