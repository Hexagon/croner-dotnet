# Croner for .NET 1.0.0

The first stable release of Croner for .NET: cron expressions and scheduled jobs, with daylight saving time handled correctly. From this release on, the public API follows semantic versioning.

```sh
dotnet add package Croner
```

Croner for .NET shares its pattern syntax, DST rules and test cases with [croner for JavaScript](https://github.com/Hexagon/croner) (11.x) and [croner-rust](https://github.com/Hexagon/croner-rust) (5.x). The same pattern gives the same runs in all three.

## Highlights

- **Patterns**: 5, 6 or 7 fields (seconds and years), `L`, `W`, `LW`, `#`, `?`, `+`, nicknames such as `@daily`, month and weekday names, and Quartz-style weekday numbering on request.
- **Next and previous occurrences** in any `TimeZoneInfo`, or enumerated lazily. Times skipped or repeated by a DST change follow well-defined rules in both directions, and a search never returns a time it has already passed.
- **Descriptions** in plain English or Swedish, or in your own language through `ICronLanguage`: `"At 07:30, on Monday, Tuesday, Wednesday, Thursday, and Friday."`
- **`CronJob`** schedules sync or async callbacks with cancellation, pause/resume/stop, `StopAsync` and `DisposeAsync`, max runs, minimum interval, overlap prevention, error handling, start/stop dates and day offsets. It takes its clock and timers from a `TimeProvider`, so schedules can be tested deterministically.
- **OCPS**: the test suite runs every conformance test vector of the [Open Cron Pattern Specification](https://github.com/open-source-cron/ocps) 1.0 to 1.4. The few deliberate deviations, which croner for JavaScript and croner-rust share, are listed in the README.
- **Fast**: finding the next occurrence takes about 70 ns in UTC and 330 ns in a time zone with DST, without allocations.
- No dependencies. Targets .NET 8 and .NET 10. Trimming- and AOT-compatible.

```csharp
using Croner;

var cron = Cron.Parse("0 30 7 * * MON-FRI");
var stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
DateTimeOffset next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, stockholm);
Console.WriteLine(cron.Describe()); // "At 07:30, on Monday, Tuesday, Wednesday, Thursday, and Friday."

await using var job = new CronJob("0 30 7 * * MON-FRI", async (job, cancellationToken) =>
{
    await SendReportAsync(cancellationToken);
}, new CronJobOptions { TimeZone = stockholm, PreventOverlap = true });
```

## Links

- [README](https://github.com/hexagon/croner-dotnet#readme): DST rules, OCPS conformance, and a comparison with Cronos, NCrontab and Quartz.NET
- [Full changelog](https://github.com/hexagon/croner-dotnet/blob/main/CHANGELOG.md)
- [NuGet](https://www.nuget.org/packages/Croner)
