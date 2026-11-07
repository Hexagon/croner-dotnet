# Changelog

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
