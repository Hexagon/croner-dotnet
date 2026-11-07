# Croner for .NET

Trigger functions and/or evaluate cron expressions in .NET. No dependencies. Most features.

A port of [croner](https://github.com/hexagon/croner) (JavaScript) and [croner-rust](https://github.com/hexagon/croner-rust), sharing their pattern syntax, DST rules and test cases.

- Parses [OCPS](https://github.com/open-source-cron/ocps)-compliant patterns: seconds and years (5, 6 or 7 fields), `L`, `W`, `LW`, `#`, `?`, `+`, nicknames like `@daily`, and month and weekday names.
- Finds next and previous occurrences in any `TimeZoneInfo`, with correct handling of daylight saving time.
- Describes patterns in plain English (or Swedish, or your own language).
- Schedules jobs, with pause/resume/stop, max runs, minimum interval, overrun protection, error handling, start/stop dates and day offsets.
- Zero dependencies. Targets .NET 8 and .NET 10. Trimming- and AOT-compatible.

## Installation

```sh
dotnet add package Croner
```

## Quick examples

```csharp
using Croner;

// Evaluate a pattern
var cron = Cron.Parse("0 0 * * FRI");
DateTimeOffset next = cron.FindNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
DateTimeOffset previous = cron.FindPreviousOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
Console.WriteLine(cron.Describe()); // "At 00:00, on Friday."

// Run a job every five seconds
using var job = new CronJob("*/5 * * * * *", _ => Console.WriteLine("This will run every fifth second"));

// Run a job once, at a given time
using var once = new CronJob(DateTimeOffset.Now.AddMinutes(1), _ => Console.WriteLine("One minute later"));

// Async callbacks, in a time zone, with options
using var report = new CronJob("0 30 7 * * MON-FRI", async j =>
{
    await SendReportAsync();
}, new CronJobOptions
{
    TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"),
    Protect = true,                            // skip a run while the previous is still busy
    OnError = (e, j) => Console.WriteLine(e),  // catch exceptions from the callback
});
```

## Evaluating patterns: `Cron`

```csharp
var cron = Cron.Parse("0 12 * * MON-FRI");                       // throws CronException when invalid
Cron.TryParse("0 12 * * MON-FRI", out Cron? maybe);              // or not

// With parser options
var quartz = Cron.Parse("0 0 12 ? * 2-6", new CronParserOptions
{
    Seconds = SecondsMode.Required,   // Optional (default), Required, Disallowed
    Year = YearMode.Optional,         // Optional (default), Required, Disallowed
    AlternativeWeekdays = true,       // Quartz numbering: 1 = Sunday ... 7 = Saturday
    DomAndDow = false,                // AND instead of OR for day-of-month and day-of-week
    SloppyRanges = false,             // accept non-standard "0/10" and "/10"
});
```

### Next and previous occurrences

Each search takes a start time and, optionally, whether that time itself may be returned (`inclusive`, default `false`). Results are whole seconds.

| Overload | Evaluates the pattern in |
| --- | --- |
| `FindNextOccurrence(DateTimeOffset start, TimeZoneInfo zone, bool inclusive = false)` | `zone`, with DST handling; the result carries the zone's offset |
| `FindNextOccurrence(DateTimeOffset start, bool inclusive = false)` | the fixed UTC offset of `start` |
| `FindNextOccurrence(DateTime start, bool inclusive = false)` | UTC for `DateTimeKind.Utc`, `TimeZoneInfo.Local` for `Local`, a plain wall clock for `Unspecified`; the result has the same kind |

`FindPreviousOccurrence` has the same overloads. Both throw `CronException` with `Kind == CronErrorKind.TimeSearchLimitExceeded` when no occurrence exists (for example `0 0 30 2 *`); `TryFindOccurrence` returns `false` instead.

```csharp
var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
var cron = Cron.Parse("0 9 * * *");

DateTimeOffset next = cron.FindNextOccurrence(DateTimeOffset.UtcNow, tz);   // 09:00 New York time, offset -04:00 or -05:00

foreach (DateTimeOffset run in cron.IterAfter(DateTimeOffset.UtcNow, tz).Take(5))
{
    Console.WriteLine(run);
}
```

`IterAfter` (exclusive, forward), `IterBefore` (exclusive, backward) and `IterFrom(start, Direction)` (inclusive) enumerate occurrences lazily; they end when the search limit is reached.

### Matching

```csharp
cron.IsTimeMatching(DateTimeOffset.Now);                        // wall clock of the value itself
cron.IsTimeMatching(DateTimeOffset.UtcNow, tz);                 // wall clock in tz
cron.IsTimeMatching(new DateTime(2025, 1, 1, 9, 0, 0));          // wall clock fields
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
p.Hours.Values();          // [9, 10, 11]
p.DaysOfWeek.Values();     // [1, 2, 3, 4, 5]  (0 = Sunday, whatever numbering the pattern used)
p.DaysOfWeek.HasModifiers; // false; true when the field uses L, W or #
p.IsDomWildcard;           // true
cron.JobType;              // FixedTime or IntervalWildcard (decides DST behaviour)
```

`Cron` and `CronPattern` compare by schedule, not by spelling: `Cron.Parse("0 0 * * MON-FRI").Equals(Cron.Parse("0 0 * * 1-5"))` is `true`.

## Scheduling jobs: `CronJob`

```csharp
var job = new CronJob("*/10 * * * * *", j => Console.WriteLine($"Run at {j.CurrentRun}"));

job.Pause();
job.Resume();
job.Stop();      // or Dispose()

await job.Trigger();   // run now, outside the schedule
```

A `CronJob` created without a callback only evaluates its schedule; `Schedule(callback)` starts it later.

| Member | Description |
| --- | --- |
| `NextRun(from?)` | The next run after `from` (default now), or `null` |
| `NextRuns(n, from?)` | The next `n` runs |
| `PreviousRuns(n, reference?)` | The previous `n` runs, most recent first |
| `Enumerate(startAt?)` | Lazily enumerates future runs |
| `TimeToNext(from?)` | Time until the next run |
| `Match(time)` | Whether a time matches the schedule |
| `IsRunning` / `IsBusy` / `IsStopped` | Scheduled and waiting / callback executing / stopped for good |
| `CurrentRun` / `PreviousRun` | Start of the current (or latest) run / latest completed run |
| `RunsLeft` | Remaining runs with `MaxRuns`, otherwise `null` |
| `Pattern` / `Once` / `Cron` | The pattern, the one-off time, the parsed `Cron` |
| `Name` / `Context` | From the options |
| `CronJob.ScheduledJobs` | All named jobs that have not been stopped |

### Options

| Option | Default | Description |
| --- | --- | --- |
| `Name` | `null` | Unique name; listed in `CronJob.ScheduledJobs` |
| `TimeZone` | `TimeZoneInfo.Local` | Time zone the pattern is evaluated in |
| `UtcOffset` | `null` | Use a fixed UTC offset instead of a time zone |
| `StartAt` / `StopAt` | `null` | Run only from `StartAt` (inclusive) until `StopAt` (exclusive) |
| `MaxRuns` | unlimited | Maximum number of scheduled runs |
| `Interval` | `0` | Minimum time between runs (whole seconds) |
| `Paused` | `false` | Start paused |
| `Protect` | `false` | Skip a run while the previous one is still busy |
| `OnBlocked` | `null` | Called when `Protect` skips a run (implies `Protect`) |
| `Catch` | `false` | Swallow exceptions from the callback |
| `OnError` | `null` | Called with exceptions from the callback (implies `Catch`) |
| `DayOffset` | `0` | Run this many days after (or, negative, before) each match |
| `AllowPast` | `false` | Run a one-off job whose time has passed right away |
| `Mode` | `Auto` | Accepted pattern lengths: `Auto`, `FivePart`, `SixPart`, `SevenPart`, `FiveOrSixParts`, `SixOrSevenParts` |
| `DomAndDow`, `AlternativeWeekdays`, `SloppyRanges` | `false` | As for `CronParserOptions` |
| `Context` | `null` | Any value, available as `job.Context` |
| `TimeProvider` | `TimeProvider.System` | The clock and timers to use, e.g. a fake one in tests |

Without `Catch` or `OnError`, an exception thrown by a scheduled run is rethrown on the thread pool, which ends the process like any unhandled exception in a timer callback. `Trigger()` always lets the exception reach its caller.

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
| Day of week | Yes | 0-7 or SUN-SAT | `* , - / ? L # +` | `5L` = last Friday, `1#2` = second Monday, `+` prefix = AND with day of month |
| Year | Optional | 1-5000 | `* , - /` | |

- Patterns are case-insensitive. Leading zeros and extra whitespace are fine.
- With 5 fields, seconds default to `0`; without a year, any year matches.
- Steps need a starting point, as in Vixie cron and OCPS: `*/10` and `5-59/10` work; `0/10` and `/10` need `SloppyRanges`.
- When both day of month and day of week are set, a day matching **either** runs (OR, as in Vixie cron). Use `DomAndDow = true`, or prefix the day of week with `+` (`0 0 1 * +MON`), to require both.

| Nickname | Equivalent |
| --- | --- |
| `@yearly`, `@annually` | `0 0 1 1 *` |
| `@monthly` | `0 0 1 * *` |
| `@weekly` | `0 0 * * 0` |
| `@daily`, `@midnight` | `0 0 * * *` |
| `@hourly` | `0 * * * *` |

## Daylight saving time

Searches walk the wall clock of the requested time zone and follow the same rules as croner and croner-rust:

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
