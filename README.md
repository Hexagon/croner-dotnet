# Croner for .NET

Croner is available for [JavaScript/TypeScript](https://github.com/Hexagon/croner) · [Rust](https://github.com/Hexagon/croner-rust) · [Python](https://github.com/Hexagon/croner-py) · **.NET** · [C++](https://github.com/Hexagon/croner-cpp)

[![NuGet](https://img.shields.io/nuget/v/Croner.svg)](https://www.nuget.org/packages/Croner) [![Build](https://github.com/hexagon/croner-dotnet/actions/workflows/build.yml/badge.svg)](https://github.com/hexagon/croner-dotnet/actions/workflows/build.yml)

Cron expressions and scheduled jobs for .NET, with daylight saving time handled correctly.

- **Parse** [OCPS](https://github.com/open-source-cron/ocps)-compliant patterns: 5, 6 or 7 fields (seconds and years), `L`, `W`, `LW`, `#`, `?`, `+`, nicknames like `@daily`, and month and weekday names. Quartz-style weekday numbering on request.
- **Search** next and previous occurrences in any `TimeZoneInfo`, or iterate them lazily. Times skipped or repeated by a DST change are handled by well-defined rules, in both directions.
- **Describe** patterns in plain English or Swedish, or your own language: `"At 07:30, on Monday, Tuesday, Wednesday, Thursday, and Friday."`
- **Schedule** sync or async callbacks with pause/resume/stop, max runs, minimum interval, overrun protection, error handling, start/stop dates and day offsets.
- **Test** schedules deterministically: jobs take their clock and timers from a `TimeProvider`.
- No dependencies. Targets .NET 8 and .NET 10. Trimming- and AOT-compatible. `Cron` is immutable and thread-safe.

## Installation

```sh
dotnet add package Croner
```

## Quick examples

```csharp
using Croner;

// Evaluate a pattern
var cron = Cron.Parse("0 0 * * FRI");
DateTimeOffset next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
DateTimeOffset previous = cron.GetPreviousOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
Console.WriteLine(cron.Describe()); // "At 00:00, on Friday."

// Run a job every five seconds
using var job = new CronJob("*/5 * * * * *", _ => Console.WriteLine("This will run every fifth second"));

// Run a job once, at a given time
using var once = new CronJob(DateTimeOffset.UtcNow.AddMinutes(1), _ => Console.WriteLine("One minute later"));

// Async callbacks with cancellation, in a time zone, with options
await using var report = new CronJob("0 30 7 * * MON-FRI", async (job, cancellationToken) =>
{
    await SendReportAsync(cancellationToken);
}, new CronJobOptions
{
    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"),
    PreventOverlap = true,                     // skip a run while the previous one is still executing
    OnError = (e, job) => Console.WriteLine(e), // handle exceptions from the callback
});
```

## Evaluating patterns: `Cron`

```csharp
var cron = Cron.Parse("0 12 * * MON-FRI");     // throws CronException when invalid

if (Cron.TryParse(userInput, out Cron? parsed))  // returns false for null or invalid input
{
    Console.WriteLine(parsed.Describe());
}

// With parser options
var quartz = Cron.Parse("0 0 12 ? * 2-6", new CronParserOptions
{
    Seconds = SecondsMode.Required,   // Optional (default), Required, Disallowed
    Year = YearMode.Optional,         // Optional (default), Required, Disallowed
    QuartzWeekdays = true,            // 1 = Sunday ... 7 = Saturday
    RequireBothDayFields = false,     // AND instead of OR for day-of-month and day-of-week
    LenientSteps = false,             // accept non-standard "0/10" and "/10"
});
```

`Cron` implements `IParsable<Cron>`, so it works with generic parsing code and configuration binders that use it.

### Next and previous occurrences

Each search takes a start time and, optionally, whether that time itself may be returned (`inclusive`, default `false`). Results are whole seconds.

| Overload | Evaluates the pattern in |
| --- | --- |
| `GetNextOccurrence(DateTimeOffset from, TimeZoneInfo zone, bool inclusive = false)` | `zone`, with DST handling; the result carries the zone's offset |
| `GetNextOccurrence(DateTimeOffset from, bool inclusive = false)` | the fixed UTC offset of `from` |
| `GetNextOccurrence(DateTime from, bool inclusive = false)` | UTC for `DateTimeKind.Utc`, `TimeZoneInfo.Local` for `Local`, a plain wall clock for `Unspecified`; the result has the same kind |

`GetPreviousOccurrence` has the same overloads. Both throw `CronException` with `Kind == CronErrorKind.SearchLimitExceeded` when no occurrence exists (for example `0 0 30 2 *`). `TryGetNextOccurrence` and `TryGetPreviousOccurrence` return `false` instead; pass `null` as the zone to use the fixed offset of `from`.

```csharp
var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
var cron = Cron.Parse("0 9 * * *");

DateTimeOffset next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, tz);   // 09:00 New York time, offset -04:00 or -05:00

if (cron.TryGetPreviousOccurrence(DateTimeOffset.UtcNow, tz, out DateTimeOffset previous))
{
    Console.WriteLine(previous);
}
```

### Enumerating occurrences

```csharp
// The next five, lazily
foreach (DateTimeOffset run in cron.GetNextOccurrences(DateTimeOffset.UtcNow, tz).Take(5))
{
    Console.WriteLine(run);
}

// The latest three, most recent first
var latest = cron.GetPreviousOccurrences(DateTimeOffset.UtcNow, tz).Take(3).ToList();

// All occurrences in a range: from inclusive, to exclusive
var thisWeek = cron.GetOccurrences(startOfWeek, startOfWeek.AddDays(7), tz).ToList();
```

`GetNextOccurrences` and `GetPreviousOccurrences` also take `inclusive`, and have `DateTime` overloads. The sequences end when the search limit is reached, so take what you need.

### Matching

```csharp
cron.IsMatch(DateTimeOffset.Now);                        // wall clock of the value itself
cron.IsMatch(DateTimeOffset.UtcNow, tz);                 // wall clock in tz
cron.IsMatch(new DateTime(2025, 1, 1, 9, 0, 0));          // wall clock fields
```

### Describing

```csharp
Cron.Parse("0 12 * * MON-FRI").Describe();
// "At 12:00, on Monday, Tuesday, Wednesday, Thursday, and Friday."

Cron.Parse("0 0 1 */2 *").Describe(Croner.Describing.Swedish.Instance);
// "Klockan 00:00, på dag 1, i var 2:e månad."
```

Implement `Croner.Describing.ICronLanguage` for other languages, or to change details such as using a 12-hour clock (`TimeOfDay`).

### Inspecting a pattern

```csharp
var p = Cron.Parse("0 9-11 * * 1-5").Pattern;
p.Expression;                 // "0 9-11 * * 1-5"
p.Hours.GetValues();          // [9, 10, 11]
p.DaysOfWeek.GetValues();     // [1, 2, 3, 4, 5]  (0 = Sunday, whatever numbering the pattern used)
p.DaysOfWeek.HasModifiers;    // false; true when the field uses L, W or #
p.IsDayOfMonthWildcard;       // true
cron.JobType;                 // FixedTime or IntervalWildcard (decides DST behaviour)
```

`Cron` and `CronPattern` compare by their parsed fields, not by spelling: `Cron.Parse("0 0 * * MON-FRI").Equals(Cron.Parse("0 0 * * 1-5"))` is `true`.

## Scheduling jobs: `CronJob`

```csharp
var job = new CronJob("*/10 * * * * *", j => Console.WriteLine($"Run at {j.CurrentRun}"));

job.Pause();
job.Resume();
await job.TriggerAsync();   // run now, outside the schedule

job.Stop();                 // stop the timer and cancel running callbacks
await job.StopAsync();      // ... and wait until they have finished
```

Callbacks can be `Action<CronJob>`, `Func<CronJob, Task>` or `Func<CronJob, CancellationToken, Task>`. The token is cancelled when the job is stopped or disposed; `DisposeAsync` waits for running callbacks like `StopAsync`.

A `CronJob` created without a callback only evaluates its schedule; `Start(callback)` starts it later.

| Member | Description |
| --- | --- |
| `GetNextRun(from?)` | The next run after `from` (default now), or `null` |
| `GetNextRuns(from?)` | Lazily enumerates future runs, respecting `MaxRuns`, `StopAt` and the other options |
| `GetPreviousRuns(before?)` | Lazily enumerates past runs, most recent first |
| `GetTimeToNextRun(from?)` | Time until the next run |
| `IsMatch(time)` | Whether a time matches the schedule |
| `IsScheduled` / `IsPaused` / `IsExecuting` / `IsStopped` | Waiting for a future run / paused / a callback is executing / stopped for good |
| `CurrentRun` / `PreviousRun` | Start of the current (or latest) run / latest completed run |
| `RemainingRuns` | Remaining runs with `MaxRuns`, otherwise `null` |
| `Cron` / `RunOnceAt` | The parsed schedule / the time of a one-off job |
| `Name` / `Context` | From the options |

### Options

| Option | Default | Description |
| --- | --- | --- |
| `Name` | `null` | A name, e.g. for logging; included in `ToString()` |
| `Parser` | `CronParserOptions.Default` | How the pattern is parsed |
| `TimeZone` | local time zone | Time zone the pattern is evaluated in |
| `UtcOffset` | `null` | Use a fixed UTC offset instead of a time zone |
| `StartAt` / `StopAt` | `null` | Run only from `StartAt` (inclusive) until `StopAt` (exclusive) |
| `MaxRuns` | unlimited | Maximum number of scheduled runs |
| `MinimumInterval` | `0` | Minimum time between runs (whole seconds) |
| `DayOffset` | `0` | Run this many days after (or, negative, before) each match |
| `StartPaused` | `false` | Create the job paused |
| `PreventOverlap` | `false` | Skip a run while the previous one is still executing |
| `OnSkipped` | `null` | Called when `PreventOverlap` skips a run (implies `PreventOverlap`) |
| `OnError` | `null` | Called with exceptions from the callback |
| `RunIfPast` | `false` | Run a one-off job whose time has passed right away |
| `Context` | `null` | Any value, available as `job.Context` |
| `TimeProvider` | `TimeProvider.System` | The clock and timers to use, e.g. a fake one in tests |

Without `OnError`, an exception thrown by a scheduled run is rethrown on the thread pool, which ends the process like any unhandled exception in a timer callback, and an exception from `TriggerAsync()` goes to the returned task. An `OperationCanceledException` thrown because the job was stopped is not an error.

A run that is due while the process is suspended, or while the clock jumps forward, runs once when the timer fires; missed runs are not repeated.

### In a hosted application

A `CronJob` runs on its own timer, so an `IHostedService` only has to start and stop it:

```csharp
builder.Services.AddHostedService<ReportService>();

sealed class ReportService : IHostedService, IAsyncDisposable
{
    private CronJob? _job;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _job = new CronJob("0 30 7 * * MON-FRI", (_, ct) => SendReportAsync(ct), new CronJobOptions
        {
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"),
            PreventOverlap = true,
            OnError = (e, _) => Console.WriteLine(e),
        });
        return Task.CompletedTask;
    }

    // Cancels a running report and waits for it, as long as the host allows.
    public Task StopAsync(CancellationToken cancellationToken) =>
        _job?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => _job?.DisposeAsync() ?? ValueTask.CompletedTask;
}
```

### Testing

Pass a `TimeProvider` to control time, for example `FakeTimeProvider` from [Microsoft.Extensions.TimeProvider.Testing](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing):

```csharp
var time = new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
int runs = 0;
using var job = new CronJob("0 * * * *", _ => runs++, new CronJobOptions
{
    TimeProvider = time,
    TimeZone = TimeZoneInfo.Utc,
});

for (int i = 0; i < 3; i++)
{
    time.Advance(TimeSpan.FromHours(1));
}
// runs == 3
```

Advance the clock one run at a time: a single `Advance(TimeSpan.FromHours(3))` is a clock jump, and runs once.

## Pattern

```
┌──────────────── (optional) second (0 - 59)
│ ┌────────────── minute (0 - 59)
│ │ ┌──────────── hour (0 - 23)
│ │ │ ┌────────── day of month (1 - 31)
│ │ │ │ ┌──────── month (1 - 12, JAN-DEC)
│ │ │ │ │ ┌────── day of week (0 - 7, SUN-SAT) (0 to 6 are Sunday to Saturday; 7 is Sunday, the same as 0)
│ │ │ │ │ │ ┌──── (optional) year (1 - 5000)
│ │ │ │ │ │ │
* * * * * * *
```

| Field | Required | Allowed values | Allowed special characters | Remarks |
| --- | --- | --- | --- | --- |
| Seconds | Optional | 0-59 | `* , - /` | |
| Minutes | Yes | 0-59 | `* , - /` | |
| Hours | Yes | 0-23 | `* , - /` | |
| Day of month | Yes | 1-31 | `* , - / ? L W` | `L` = last day, `15W` = weekday nearest the 15th, `LW` = last weekday |
| Month | Yes | 1-12 or JAN-DEC | `* , - /` | |
| Day of week | Yes | 0-7 or SUN-SAT | `* , - / ? L # +` | `5L` = last Friday, `1#2` = second Monday, `+` prefix = AND with day of month (`L` needs a weekday here) |
| Year | Optional | 1-5000 | `* , - /` | |

- Patterns are case-insensitive. Leading zeros and extra whitespace are fine. Month names are only accepted in the month field, and weekday names in the day-of-week field.
- With 5 fields, seconds default to `0`; without a year, any year matches.
- Steps need a starting point, as in Vixie cron and OCPS: `*/10` and `5-59/10` work; `0/10` and `/10` need `LenientSteps`.
- When both day of month and day of week are set, a day matching **either** runs (OR, as in Vixie cron). Use `RequireBothDayFields = true`, or prefix the day of week with `+` (`0 0 1 * +MON`), to require both.

| Nickname | Equivalent |
| --- | --- |
| `@yearly`, `@annually` | `0 0 1 1 *` |
| `@monthly` | `0 0 1 * *` |
| `@weekly` | `0 0 * * 0` |
| `@daily`, `@midnight` | `0 0 * * *` |
| `@hourly` | `0 * * * *` |

## Daylight saving time

Patterns describe wall clock time, but a time zone's clock sometimes skips an hour (spring forward) or repeats one (fall back). Searches walk the wall clock of the requested time zone and resolve those hours by the kind of pattern (`Cron.JobType`):

- **Fixed-time jobs** (single second, minute and hour, e.g. `0 30 2 * * *`):
  - when the clock jumps forward over the run time, the job runs at the first instant after the jump;
  - when the clock goes back and the run time happens twice, the job runs once, at the first occurrence.
- **Interval and wildcard jobs** (everything else, e.g. `*/5 * * * *`):
  - times skipped by a jump forward are skipped;
  - times that happen twice run twice, in real order.

Searches never return a time they have already passed, also when starting inside a repeated hour, in either direction.

## Development

```sh
dotnet build
dotnet test
dotnet pack src/Croner -c Release
```

Releases are published to NuGet by `.github/workflows/build.yml` through trusted publishing: push a tag such as `v1.0.0` and the package is built, tested and pushed with that version.

## License

MIT
