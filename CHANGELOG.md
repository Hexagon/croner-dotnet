# Changelog

## 1.0.0

First release. A .NET port of croner (JavaScript) and croner-rust.

- `Cron`: parse OCPS-compliant patterns (5-7 fields, `L`, `W`, `LW`, `#`, `?`, `+`, nicknames), match times,
  find next/previous occurrences and iterate them in any `TimeZoneInfo` with croner's DST rules.
- `Cron.Describe()`: plain-language descriptions in English and Swedish, extensible through `ICronLanguage`.
- `CronJob`: schedule callbacks with pause/resume/stop, max runs, interval, overrun protection, error handling,
  start/stop dates, day offsets, one-off dates and `TimeProvider` support.
- No dependencies. Targets .NET 8 and .NET 10.
