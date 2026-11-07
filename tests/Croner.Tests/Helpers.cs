using System;

namespace Croner.Tests;

internal static class H
{
    /// <summary>A plain wall clock time (no time zone).</summary>
    public static DateTime D(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) =>
        new(y, mo, d, h, mi, s, DateTimeKind.Unspecified);

    public static TimeZoneInfo Tz(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    /// <summary>The single instant a wall clock time denotes in a zone.</summary>
    public static DateTimeOffset At(string zone, int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
    {
        var tz = Tz(zone);
        var civil = D(y, mo, d, h, mi, s);
        if (tz.IsInvalidTime(civil) || tz.IsAmbiguousTime(civil))
        {
            throw new InvalidOperationException($"{civil} is not a single instant in {zone}");
        }
        return new DateTimeOffset(civil, tz.GetUtcOffset(civil));
    }

    /// <summary>The earlier (first) or later (second) instant of a repeated wall clock time.</summary>
    public static DateTimeOffset Ambiguous(string zone, bool first, int y, int mo, int d, int h, int mi = 0, int s = 0)
    {
        var tz = Tz(zone);
        var civil = D(y, mo, d, h, mi, s);
        if (!tz.IsAmbiguousTime(civil))
        {
            throw new InvalidOperationException($"{civil} does not repeat in {zone}");
        }
        var offsets = tz.GetAmbiguousTimeOffsets(civil);
        var max = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
        var min = offsets[0] > offsets[1] ? offsets[1] : offsets[0];
        return new DateTimeOffset(civil, first ? max : min);
    }

    public static Cron Seconds(string pattern) =>
        Cron.Parse(pattern, new CronParserOptions { Seconds = SecondsMode.Optional });
}
