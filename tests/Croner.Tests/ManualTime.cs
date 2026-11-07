using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Croner.Tests;

/// <summary>A minimal controllable clock: timers fire only when <see cref="Advance"/> is called.</summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now;

    public ManualTime(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
        {
            return _now;
        }
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward, firing due timers in order.</summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset end;
        lock (_lock)
        {
            end = _now + by;
        }
        while (true)
        {
            ManualTimer? next;
            lock (_lock)
            {
                next = _timers.Where(t => t.Due is not null && t.Due <= end).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    _now = end;
                    return;
                }
                if (next.Due > _now)
                {
                    _now = next.Due!.Value;
                }
                next.Due = null;
            }
            next.Fire();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTime _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;

        public ManualTimer(ManualTime owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            lock (owner._lock)
            {
                owner._timers.Add(this);
            }
        }

        public DateTimeOffset? Due { get; set; }

        public void Fire() => _callback(_state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._lock)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : _owner._now + dueTime;
            }
            return true;
        }

        public void Dispose()
        {
            lock (_owner._lock)
            {
                Due = null;
                _owner._timers.Remove(this);
            }
        }

        public System.Threading.Tasks.ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
