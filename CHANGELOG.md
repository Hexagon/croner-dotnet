# Changelog

## 1.0.0 (unreleased)

The first stable release. The pattern syntax, DST rules and test cases match croner for JavaScript 11.0.1
(and its unreleased fixes) and croner-rust 5.0.1 (and its unreleased fixes). From this release on, the
public API follows semantic versioning.

### Breaking changes since 1.0.0-preview.2

- Stricter parsing, as in croner for JavaScript and croner-rust. Patterns that used to parse and run on an
  unintended schedule now throw `CronException`:
  - Weekday and month names have to stand alone: `SUN6` (read as 6), `JANFEB` (read as 12), `*/FRI`
    (read as `*/5`), `5#FRI`, `DEC1` and `MONTUE` are rejected. `MON-FRI/2`, `JAN,FEB`, `FRI#2`, `FRI#L`
    and `FRIL` work as before.
  - A number is digits only: `15W5` (read as `15W`), `LW5`, `5LL` and `5L#2` are rejected.
  - A list has no empty items: `15,,L`, `L,` and `,L` are rejected.
- `CronComponent.Min`, `Max` and `Step` are `int` instead of `ushort`, as is usual in .NET APIs.
- `CronPattern` equality includes the new `FixedTimeRule`.

### Added

- `CronParserOptions.FixedTimeRule` and `CronPattern.FixedTimeRule`: which patterns count as fixed-time jobs
  in DST handling. `SingleValue` (default) is unchanged; `NoWildcard` also counts lists and ranges without
  `*` or a step, closer to Vixie cron (as `fixedTimeRule: "no-wildcard"` in croner for JavaScript);
  `All` makes every pattern a fixed-time job, the behaviour of croner 2.x (as `FixedTimeRule::All` in
  croner-rust).
- `L` and `LW` can be combined in a day-of-month list: `L,LW` runs on the last day and the last weekday.
- `1-5L` (the last Monday to Friday of the month) is accepted, as in croner for JavaScript.
- `ICronLanguage.TheLastWorkingDayOfTheMonth`, with an English default and a Swedish translation.
- The test suite runs the OCPS 1.0 to 1.4 conformance test vectors. The README documents the conformance
  and the deliberate deviations, and compares Croner with Cronos, NCrontab and Quartz.NET.
- A BenchmarkDotNet project in `benchmarks/`.
- A package icon.

### Fixed

- Fixed-time jobs whose time falls in a DST gap longer than two hours, such as the day `Pacific/Apia`
  skipped on 2011-12-30, failed with `SearchLimitExceeded`. The end of a gap is now found by stepping out
  of it and bisecting back, for gaps of up to a week.
- Wall clock times are resolved through the zone's conversion from UTC only. `TimeZoneInfo.IsInvalidTime`
  and `IsAmbiguousTime` miss transitions that change the base offset, so in `Pacific/Apia` a search could
  return a time on the skipped day, which lies before its start.
- `SUN-SUN` is Sunday only; it was read as `0-7`, every day.
- With `QuartzWeekdays`, day-of-week ranges whose end is before their start wrap around the week, as in
  Quartz: `SAT-SUN`, `MON-SUN` and `FRI-MON` used to fail, and `FRI-TUE/2` is Friday, Sunday and Tuesday.
- `L,LW` matched only the last weekday: `LW` was stored as `L` plus a weekday flag.
- `Describe()` calls `LW` "the last weekday of the month" instead of "the last day of the month", and only
  calls a stepped field "every N" when it holds the values of `*/N`: `0-10/2 * * * *` was "At every 2
  minutes.", and `*/15,7` dropped the 7.
- `CronJob.GetPreviousRuns` from a reference with a fraction of a second includes the run earlier in the
  same second, as in croner for JavaScript 11.0.1. With `MinimumInterval`, runs are at least that long before
  the reference.

### Performance

- A search in a time zone is about 4x faster: `TimeZoneInfo.GetAdjustmentRules()`, which copies every rule,
  was called on each search.
- Parsing is about 7x faster (`*/5 * * * *`: from about 21 µs to 3 µs): a `*` field, such as the default
  year field with its 5,000 values, is filled in one pass.

## 1.0.0-preview.2

Second preview of the first release. Shares its pattern syntax, DST rules and test cases with the other Croner implementations.

- `Cron`: parse OCPS-compliant patterns (5-7 fields, `L`, `W`, `LW`, `#`, `?`, `+`, nicknames), match times,
  get next/previous occurrences and enumerate them in any `TimeZoneInfo` with well-defined DST rules.
  Implements `IParsable<Cron>`.
- `Cron.Describe()`: plain-language descriptions in English and Swedish, extensible through `ICronLanguage`.
- `CronJob`: schedule sync or async callbacks with cancellation, pause/resume/stop, `StopAsync`/`DisposeAsync` that
  wait for running callbacks, max runs, minimum interval, overlap prevention, error handling, start/stop dates,
  day offsets, one-off times and `TimeProvider` support.
- No dependencies. Targets .NET 8 and .NET 10. Trimming- and AOT-compatible.

### Changes since 1.0.0-preview.1

The API was reworked to follow .NET conventions. Renamed members:

| 1.0.0-preview.1 | 1.0.0-preview.2 |
| --- | --- |
| `Cron.FindNextOccurrence` / `FindPreviousOccurrence` | `GetNextOccurrence` / `GetPreviousOccurrence` |
| `Cron.TryFindOccurrence(start, zone, inclusive, direction, out)` | `TryGetNextOccurrence` / `TryGetPreviousOccurrence` |
| `Cron.IterAfter` / `IterBefore` / `IterFrom` | `GetNextOccurrences` / `GetPreviousOccurrences` (with `inclusive`), new `GetOccurrences(from, to)` |
| `Cron.IsTimeMatching`, `CronJob.Match` | `IsMatch` |
| `Cron.YearUpperLimit` / `YearLowerLimit` | `Cron.MaxYear` / `Cron.MinYear` |
| `CronPattern.Pattern` | `CronPattern.Expression` |
| `CronPattern.Days`, `IsDomWildcard`, `IsDowWildcard`, `MatchesDomAndDow` | `DaysOfMonth`, `IsDayOfMonthWildcard`, `IsDayOfWeekWildcard`, `RequiresBothDayFields` |
| `CronComponent.Values()` | `GetValues()` |
| `ICronLanguage.EverySecondPhrase` / `DomAndDowIfAlso` / `DomAndDowIfAlsoOneOf` | `EverySecond` / `IfAlsoDayOfWeek` / `IfAlsoOneOfDaysOfWeek` |
| `CronParserOptions.DomAndDow` / `AlternativeWeekdays` / `SloppyRanges` | `RequireBothDayFields` / `QuartzWeekdays` / `LenientSteps` |
| `CronErrorKind.TimeSearchLimitExceeded` / `ComponentError` / `IllegalCharacters` | `SearchLimitExceeded` / `InvalidField` / `IllegalCharacter` |
| `CronJob.NextRun` / `NextRuns(n)` / `Enumerate` / `PreviousRuns(n)` / `TimeToNext` | `GetNextRun` / `GetNextRuns().Take(n)` / `GetNextRuns` / `GetPreviousRuns().Take(n)` / `GetTimeToNextRun` |
| `CronJob.IsRunning` / `IsBusy` / `RunsLeft` / `Once` | `IsScheduled` / `IsExecuting` / `RemainingRuns` / `RunOnceAt` |
| `CronJob.Schedule(callback)` / `Trigger()` | `Start(callback)` / `TriggerAsync(cancellationToken)` |
| `CronJobOptions.Interval` / `Paused` / `Protect` / `OnBlocked` / `AllowPast` | `MinimumInterval` / `StartPaused` / `PreventOverlap` / `OnSkipped` / `RunIfPast` |
| `CronJobOptions.Mode`, `DomAndDow`, `AlternativeWeekdays`, `SloppyRanges` | `CronJobOptions.Parser` (a `CronParserOptions`) |

Removed:

- `CronJobOptions.Catch`: set `OnError`, for example to `(_, _) => { }`.
- `CronJob.ScheduledJobs` and unique job names: keep references to your jobs, for example in DI. `Name` remains, for logging.
- `CronJob.Pattern`: use `job.Cron`.
- One-off jobs from ISO 8601 strings: pass a `DateTimeOffset`.
- `CronParser`, `Describer` and `Direction` are no longer public: use `Cron.Parse` and `Cron.Describe`.

Added:

- Async callbacks can take a `CancellationToken`, cancelled when the job is stopped or disposed.
- `CronJob.StopAsync()` and `IAsyncDisposable`: stop and wait for running callbacks.
- `CronJob.IsPaused`, `CronJob.ToString()`.
- `Cron` implements `IParsable<Cron>`.
- `Cron.GetOccurrences(from, to)` for a range.

Fixed:

- Month names are only accepted in the month field, and weekday names in the day-of-week field;
  `0 0 * MON *` used to be read as January.
- `L` alone in the day-of-week field is rejected; it never matched.
- `IsExecuting` stays `true` until all overlapping runs have finished.
- `Cron.TryParse` returns `false` for a `null` pattern instead of throwing, and is annotated for nullable analysis.
- Patterns parsed with different weekday numbering or step options compare equal when their fields are equal.
- `ICronLanguage.DayOfWeekNames` and `MonthNames` are `IReadOnlyList<string>`, so the built-in languages cannot be changed by accident.
- The default time zone of a `CronJob` comes from its `TimeProvider`.
- Clearer `CronException` messages.
- Source Link, deterministic CI builds and package validation.
