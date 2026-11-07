# Changelog

## 1.0.0

First stable release. Shares its pattern syntax, DST rules and test cases with the other Croner implementations.

- `Cron`: parse OCPS-compliant patterns (5-7 fields, `L`, `W`, `LW`, `#`, `?`, `+`, nicknames), match times,
  find next/previous occurrences and iterate them in any `TimeZoneInfo` with croner's DST rules.
- `Cron.Describe()`: plain-language descriptions in English and Swedish, extensible through `ICronLanguage`.
- `CronJob`: schedule callbacks with pause/resume/stop, max runs, interval, overrun protection, error handling,
  start/stop dates, day offsets, one-off dates and `TimeProvider` support.
- No dependencies. Targets .NET 8 and .NET 10.

Changes since 1.0.0-preview.1:

- Month names are only accepted in the month field, and weekday names in the day-of-week field;
  `0 0 * MON *` used to be read as January.
- `Cron.TryParse` returns `false` for a `null` pattern instead of throwing, and is annotated for nullable analysis.
- Patterns parsed with different `AlternativeWeekdays` or `SloppyRanges` options compare equal when their fields are equal.
- Clearer `CronException` messages.
- Source Link, deterministic CI builds and package validation.
