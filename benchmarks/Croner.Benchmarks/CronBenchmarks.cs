using BenchmarkDotNet.Attributes;
using Croner;

namespace Croner.Benchmarks;

/// <summary>Parsing a pattern, compared with Cronos and NCrontab.</summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    [Benchmark(Baseline = true)]
    public Cron Croner() => Cron.Parse("*/5 * * * *");

    [Benchmark]
    public Cronos.CronExpression Cronos() => global::Cronos.CronExpression.Parse("*/5 * * * *");

    [Benchmark]
    public NCrontab.CrontabSchedule NCrontab() => global::NCrontab.CrontabSchedule.Parse("*/5 * * * *");
}

/// <summary>Finding the next occurrence, in UTC and in a time zone with DST.</summary>
[MemoryDiagnoser]
public class NextOccurrenceBenchmarks
{
    private static readonly DateTimeOffset From = new(2026, 5, 13, 10, 17, 23, TimeSpan.Zero);
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    private readonly Cron _every5 = Cron.Parse("*/5 * * * *");
    private readonly Cron _weekdays = Cron.Parse("0 30 9 * * 1-5");
    private readonly Cronos.CronExpression _cronosEvery5 = Cronos.CronExpression.Parse("*/5 * * * *");
    private readonly Cronos.CronExpression _cronosWeekdays = Cronos.CronExpression.Parse("0 30 9 * * 1-5", Cronos.CronFormat.IncludeSeconds);
    private readonly NCrontab.CrontabSchedule _ncrontabEvery5 = NCrontab.CrontabSchedule.Parse("*/5 * * * *");

    [Benchmark(Baseline = true)]
    public DateTimeOffset CronerUtc() => _every5.GetNextOccurrence(From, TimeZoneInfo.Utc);

    [Benchmark]
    public DateTime? CronosUtc() => _cronosEvery5.GetNextOccurrence(From.UtcDateTime);

    [Benchmark]
    public DateTime NCrontabUtc() => _ncrontabEvery5.GetNextOccurrence(From.UtcDateTime);

    [Benchmark]
    public DateTimeOffset CronerTimeZone() => _weekdays.GetNextOccurrence(From, Stockholm);

    [Benchmark]
    public DateTimeOffset? CronosTimeZone() => _cronosWeekdays.GetNextOccurrence(From, Stockholm);
}
