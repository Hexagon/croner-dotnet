using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Croner;

/// <summary>The direction of a time search.</summary>
internal enum Direction
{
    Forward,
    Backward,
}

/// <summary>
/// Categorizes a cron pattern as either a fixed-time job or an interval/wildcard job.
/// This decides how daylight saving time transitions are handled.
/// </summary>
public enum JobType
{
    /// <summary>
    /// Seconds, minutes and hours are each a single value (for example <c>0 30 2 * * *</c>).
    /// Such a job runs once when its time repeats (fall back), at the first occurrence, and runs
    /// right after the jump when its time is skipped (spring forward).
    /// </summary>
    FixedTime,

    /// <summary>
    /// Any other pattern (for example <c>*/5 * * * *</c>). Such a job runs on both occurrences of
    /// a repeated time, and skips times that do not exist.
    /// </summary>
    IntervalWildcard,
}

/// <summary>
/// A parsed cron schedule. Use it to check whether a time matches, get next and previous
/// occurrences, enumerate occurrences, and describe the schedule in plain language.
/// Instances are immutable and thread-safe.
/// </summary>
/// <example>
/// <code>
/// var cron = Cron.Parse("0 0 * * FRI");
/// DateTimeOffset next = cron.GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
/// </code>
/// </example>
public sealed class Cron : IEquatable<Cron>, IParsable<Cron>
{
    /// <summary>
    /// The latest year a search reaches. Prevents endless searches for patterns that never match.
    /// </summary>
    public const int MaxYear = 5000;

    /// <summary>The earliest year a search reaches (1 AD).</summary>
    public const int MinYear = 1;

    private const int MaxSearchIterations = 366 * 24 * 60 * 60;
    private const int MaxGapSearchSeconds = 3600 * 2;
    private const long MaxOverlapSeconds = 86_400;

    internal Cron(CronPattern pattern)
    {
        Pattern = pattern;
        JobType = ComputeJobType(pattern);
    }

    /// <summary>The parsed pattern.</summary>
    public CronPattern Pattern { get; }

    /// <summary>Whether the pattern is a fixed-time or an interval/wildcard job. See <see cref="Croner.JobType"/>.</summary>
    public JobType JobType { get; }

    // ------------------------------------------------------------------
    // Parsing
    // ------------------------------------------------------------------

    /// <summary>Parses a cron pattern with the given options, or the defaults.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <c>null</c>.</exception>
    /// <exception cref="CronException">The pattern is invalid.</exception>
    public static Cron Parse(string pattern, CronParserOptions? options = null) =>
        new CronParser(options).Parse(pattern);

    /// <summary>Tries to parse a cron pattern with the default options. Returns <c>false</c> when it is <c>null</c> or invalid.</summary>
    public static bool TryParse([NotNullWhen(true)] string? pattern, [NotNullWhen(true)] out Cron? cron) =>
        new CronParser().TryParse(pattern, out cron);

    /// <summary>Tries to parse a cron pattern with the given options. Returns <c>false</c> when it is <c>null</c> or invalid.</summary>
    public static bool TryParse([NotNullWhen(true)] string? pattern, CronParserOptions? options, [NotNullWhen(true)] out Cron? cron) =>
        new CronParser(options).TryParse(pattern, out cron);

    /// <inheritdoc />
    static Cron IParsable<Cron>.Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <inheritdoc />
    static bool IParsable<Cron>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Cron result) =>
        TryParse(s, out result);

    // ------------------------------------------------------------------
    // Matching
    // ------------------------------------------------------------------

    /// <summary>
    /// Checks whether the wall clock time of <paramref name="time"/> (in its own offset) matches the pattern.
    /// Sub-second precision is ignored.
    /// </summary>
    public bool IsMatch(DateTimeOffset time) => IsCivilMatching(Truncate(time.DateTime));

    /// <summary>
    /// Checks whether <paramref name="time"/>, as wall clock time in <paramref name="zone"/>, matches the pattern.
    /// </summary>
    public bool IsMatch(DateTimeOffset time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return IsCivilMatching(Truncate(TimeZoneInfo.ConvertTime(time, zone).DateTime));
    }

    /// <summary>
    /// Checks whether the wall clock fields of <paramref name="time"/> match the pattern,
    /// regardless of its <see cref="DateTime.Kind"/>.
    /// </summary>
    public bool IsMatch(DateTime time) => IsCivilMatching(Truncate(time));

    private bool IsCivilMatching(DateTime c) =>
        Pattern.SecondMatch(c.Second)
        && Pattern.MinuteMatch(c.Minute)
        && Pattern.HourMatch(c.Hour)
        && Pattern.DayMatch(c.Year, c.Month, c.Day, c.DayOfWeek)
        && Pattern.MonthMatch(c.Month)
        && Pattern.YearMatch(c.Year);

    // ------------------------------------------------------------------
    // Next / previous
    // ------------------------------------------------------------------

    /// <summary>
    /// Gets the next occurrence after <paramref name="from"/>, evaluated as wall clock time in
    /// <paramref name="zone"/>, with correct handling of daylight saving time transitions.
    /// The result carries the zone's UTC offset at that instant.
    /// </summary>
    /// <param name="from">Where to start searching.</param>
    /// <param name="zone">The time zone the pattern is evaluated in.</param>
    /// <param name="inclusive">Whether <paramref name="from"/> itself (truncated to the second) may be returned.</param>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset from, TimeZoneInfo zone, bool inclusive = false) =>
        FindOccurrence(from, Zone.Of(zone), inclusive, Direction.Forward);

    /// <summary>
    /// Gets the next occurrence after <paramref name="from"/>, evaluated in the fixed UTC offset of <paramref name="from"/>.
    /// </summary>
    /// <param name="from">Where to start searching.</param>
    /// <param name="inclusive">Whether <paramref name="from"/> itself (truncated to the second) may be returned.</param>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset from, bool inclusive = false) =>
        FindOccurrence(from, Zone.Fixed(from.Offset), inclusive, Direction.Forward);

    /// <summary>
    /// Gets the next occurrence after <paramref name="from"/>. A <see cref="DateTimeKind.Utc"/> value is evaluated in UTC,
    /// a <see cref="DateTimeKind.Local"/> value in <see cref="TimeZoneInfo.Local"/>, and an
    /// <see cref="DateTimeKind.Unspecified"/> value as a plain wall clock without time zone. The result has the same kind.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTime GetNextOccurrence(DateTime from, bool inclusive = false) =>
        FromKind(FindOccurrence(ToOffset(from, out Zone zone), zone, inclusive, Direction.Forward), from.Kind);

    /// <summary>
    /// Gets the previous occurrence before <paramref name="from"/>, evaluated as wall clock time in <paramref name="zone"/>.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTimeOffset GetPreviousOccurrence(DateTimeOffset from, TimeZoneInfo zone, bool inclusive = false) =>
        FindOccurrence(from, Zone.Of(zone), inclusive, Direction.Backward);

    /// <summary>
    /// Gets the previous occurrence before <paramref name="from"/>, evaluated in the fixed UTC offset of <paramref name="from"/>.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTimeOffset GetPreviousOccurrence(DateTimeOffset from, bool inclusive = false) =>
        FindOccurrence(from, Zone.Fixed(from.Offset), inclusive, Direction.Backward);

    /// <summary>
    /// Gets the previous occurrence before <paramref name="from"/>. See <see cref="GetNextOccurrence(DateTime, bool)"/>
    /// for how the <see cref="DateTime.Kind"/> is interpreted.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTime GetPreviousOccurrence(DateTime from, bool inclusive = false) =>
        FromKind(FindOccurrence(ToOffset(from, out Zone zone), zone, inclusive, Direction.Backward), from.Kind);

    /// <summary>
    /// Tries to get the next occurrence after <paramref name="from"/>. Returns <c>false</c> when none exists within the search limits.
    /// </summary>
    /// <param name="from">Where to start searching.</param>
    /// <param name="zone">The time zone to evaluate in, or <c>null</c> for the fixed offset of <paramref name="from"/>.</param>
    /// <param name="result">The occurrence found.</param>
    public bool TryGetNextOccurrence(DateTimeOffset from, TimeZoneInfo? zone, out DateTimeOffset result) =>
        TryFind(from, zone, false, Direction.Forward, out result);

    /// <summary>
    /// Tries to get the next occurrence at or after <paramref name="from"/> (with <paramref name="inclusive"/>) or after it.
    /// Returns <c>false</c> when none exists within the search limits.
    /// </summary>
    public bool TryGetNextOccurrence(DateTimeOffset from, TimeZoneInfo? zone, bool inclusive, out DateTimeOffset result) =>
        TryFind(from, zone, inclusive, Direction.Forward, out result);

    /// <summary>
    /// Tries to get the previous occurrence before <paramref name="from"/>. Returns <c>false</c> when none exists within the search limits.
    /// </summary>
    /// <param name="from">Where to start searching.</param>
    /// <param name="zone">The time zone to evaluate in, or <c>null</c> for the fixed offset of <paramref name="from"/>.</param>
    /// <param name="result">The occurrence found.</param>
    public bool TryGetPreviousOccurrence(DateTimeOffset from, TimeZoneInfo? zone, out DateTimeOffset result) =>
        TryFind(from, zone, false, Direction.Backward, out result);

    /// <summary>
    /// Tries to get the previous occurrence at or before <paramref name="from"/> (with <paramref name="inclusive"/>) or before it.
    /// Returns <c>false</c> when none exists within the search limits.
    /// </summary>
    public bool TryGetPreviousOccurrence(DateTimeOffset from, TimeZoneInfo? zone, bool inclusive, out DateTimeOffset result) =>
        TryFind(from, zone, inclusive, Direction.Backward, out result);

    private bool TryFind(DateTimeOffset from, TimeZoneInfo? zone, bool inclusive, Direction direction, out DateTimeOffset result)
    {
        try
        {
            result = FindOccurrence(from, ZoneFor(from, zone), inclusive, direction);
            return true;
        }
        catch (CronException)
        {
            result = default;
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Enumeration
    // ------------------------------------------------------------------

    /// <summary>
    /// Lazily enumerates occurrences after <paramref name="from"/>, towards the future.
    /// The sequence ends when the search limit is reached.
    /// </summary>
    /// <param name="from">Where to start.</param>
    /// <param name="zone">The time zone to evaluate in, or <c>null</c> for the fixed offset of <paramref name="from"/>.</param>
    /// <param name="inclusive">Whether <paramref name="from"/> itself may be the first occurrence.</param>
    public IEnumerable<DateTimeOffset> GetNextOccurrences(DateTimeOffset from, TimeZoneInfo? zone = null, bool inclusive = false) =>
        Iterate(from, ZoneFor(from, zone), inclusive, Direction.Forward);

    /// <summary>
    /// Lazily enumerates occurrences before <paramref name="from"/>, towards the past, most recent first.
    /// </summary>
    /// <param name="from">Where to start.</param>
    /// <param name="zone">The time zone to evaluate in, or <c>null</c> for the fixed offset of <paramref name="from"/>.</param>
    /// <param name="inclusive">Whether <paramref name="from"/> itself may be the first occurrence.</param>
    public IEnumerable<DateTimeOffset> GetPreviousOccurrences(DateTimeOffset from, TimeZoneInfo? zone = null, bool inclusive = false) =>
        Iterate(from, ZoneFor(from, zone), inclusive, Direction.Backward);

    /// <summary>Lazily enumerates occurrences after <paramref name="from"/>. See <see cref="GetNextOccurrence(DateTime, bool)"/> for kind handling.</summary>
    public IEnumerable<DateTime> GetNextOccurrences(DateTime from, bool inclusive = false) =>
        IterateKind(from, inclusive, Direction.Forward);

    /// <summary>Lazily enumerates occurrences before <paramref name="from"/>, most recent first. See <see cref="GetNextOccurrence(DateTime, bool)"/> for kind handling.</summary>
    public IEnumerable<DateTime> GetPreviousOccurrences(DateTime from, bool inclusive = false) =>
        IterateKind(from, inclusive, Direction.Backward);

    /// <summary>
    /// Lazily enumerates the occurrences from <paramref name="from"/> (inclusive) up to <paramref name="to"/> (exclusive).
    /// </summary>
    /// <param name="from">The start of the range, inclusive.</param>
    /// <param name="to">The end of the range, exclusive.</param>
    /// <param name="zone">The time zone to evaluate in, or <c>null</c> for the fixed offset of <paramref name="from"/>.</param>
    public IEnumerable<DateTimeOffset> GetOccurrences(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo? zone = null)
    {
        foreach (DateTimeOffset occurrence in GetNextOccurrences(from, zone, inclusive: true))
        {
            if (occurrence >= to)
            {
                yield break;
            }
            yield return occurrence;
        }
    }

    private IEnumerable<DateTime> IterateKind(DateTime start, bool inclusive, Direction direction)
    {
        DateTimeOffset offsetStart = ToOffset(start, out Zone zone);
        foreach (DateTimeOffset value in Iterate(offsetStart, zone, inclusive, direction))
        {
            yield return FromKind(value, start.Kind);
        }
    }

    private IEnumerable<DateTimeOffset> Iterate(DateTimeOffset start, Zone zone, bool inclusive, Direction direction)
    {
        DateTimeOffset current = start;
        bool first = true;
        while (true)
        {
            bool inclusiveSearch = first && inclusive;
            first = false;
            DateTimeOffset found;
            try
            {
                found = FindOccurrence(current, zone, inclusiveSearch, direction);
            }
            catch (CronException)
            {
                yield break;
            }
            yield return found;
            current = found;
        }
    }

    // ------------------------------------------------------------------
    // Describe
    // ------------------------------------------------------------------

    /// <summary>Returns a human-readable English description, e.g. <c>"At 12:00, on Monday."</c></summary>
    public string Describe() => Pattern.Describe();

    /// <summary>Returns a human-readable description in the given language.</summary>
    public string Describe(Describing.ICronLanguage language) => Pattern.Describe(language);

    /// <summary>The (normalized) pattern string.</summary>
    public override string ToString() => Pattern.ToString();

    /// <inheritdoc />
    public bool Equals(Cron? other) => other is not null && Pattern.Equals(other.Pattern);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Cron);

    /// <inheritdoc />
    public override int GetHashCode() => Pattern.GetHashCode();

    // ------------------------------------------------------------------
    // Search internals
    // ------------------------------------------------------------------

    private static JobType ComputeJobType(CronPattern p)
    {
        static bool IsFixed(CronComponent f) => f.Step == 1 && !f.FromWildcard && f.CountSetValues(CronBits.All) == 1;
        return IsFixed(p.Seconds) && IsFixed(p.Minutes) && IsFixed(p.Hours) ? JobType.FixedTime : JobType.IntervalWildcard;
    }

    private static Zone ZoneFor(DateTimeOffset start, TimeZoneInfo? zone) =>
        zone is null ? Zone.Fixed(start.Offset) : Zone.Of(zone);

    private static DateTimeOffset ToOffset(DateTime start, out Zone zone)
    {
        switch (start.Kind)
        {
            case DateTimeKind.Utc:
                zone = Zone.Utc;
                return new DateTimeOffset(start, TimeSpan.Zero);
            case DateTimeKind.Local:
                zone = Zone.Of(TimeZoneInfo.Local);
                return new DateTimeOffset(start);
            default:
                zone = Zone.Utc;
                return new DateTimeOffset(start, TimeSpan.Zero);
        }
    }

    private static DateTime FromKind(DateTimeOffset value, DateTimeKind kind) => kind switch
    {
        DateTimeKind.Utc => value.UtcDateTime,
        DateTimeKind.Local => DateTime.SpecifyKind(value.DateTime, DateTimeKind.Local),
        _ => value.DateTime,
    };

    private static DateTime Truncate(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);

    private static int Step(Direction d) => d == Direction.Forward ? 1 : -1;

    /// <summary>Whether <paramref name="a"/> comes before <paramref name="b"/> in the search direction.</summary>
    private static bool Precedes(Direction d, DateTimeOffset a, DateTimeOffset b) =>
        d == Direction.Forward ? a.UtcTicks < b.UtcTicks : a.UtcTicks > b.UtcTicks;

    private enum Fold
    {
        None,
        Earlier,
        Later,
    }

    private static Fold FoldOf(DateTimeOffset instant, Zone zone)
    {
        Resolution r = zone.Resolve(Truncate(instant.DateTime));
        if (r.Kind != ResolutionKind.Ambiguous)
        {
            return Fold.None;
        }
        return instant.UtcTicks < r.Later.UtcTicks ? Fold.Earlier : Fold.Later;
    }

    /// <summary>
    /// Finds the first instant on the other half of the repeated range that <paramref name="origin"/> sits in,
    /// by bisection over at most one day.
    /// </summary>
    private static DateTimeOffset OtherFoldEdge(DateTimeOffset origin, Fold on, Zone zone)
    {
        int step = on == Fold.Earlier ? 1 : -1;
        DateTimeOffset AtOffset(long offset) =>
            zone.AddSeconds(origin, offset * step) ?? throw new CronException(CronErrorKind.InvalidTime);
        bool IsInside(long offset) => FoldOf(AtOffset(offset), zone) == on;

        if (IsInside(MaxOverlapSeconds))
        {
            throw new CronException(CronErrorKind.SearchLimitExceeded);
        }
        long inside = 0, outside = MaxOverlapSeconds;
        while (outside - inside > 1)
        {
            long middle = inside + (outside - inside) / 2;
            if (IsInside(middle))
            {
                inside = middle;
            }
            else
            {
                outside = middle;
            }
        }
        return AtOffset(outside);
    }

    /// <summary>
    /// Returns the first time that matches the pattern, counting from <paramref name="start"/> in
    /// <paramref name="direction"/> along the absolute time line.
    /// </summary>
    /// <remarks>
    /// A clock that goes back repeats a range of wall clock times, and one wall clock walk covers only
    /// one half of it. When the start sits on the half the search meets first, the other half still lies
    /// ahead: the search walks it too, from its edge, and keeps the nearer match.
    /// </remarks>
    internal DateTimeOffset FindOccurrence(DateTimeOffset start, Zone zone, bool inclusive, Direction direction)
    {
        DateTimeOffset origin = zone.ToZone(start);
        bool fixedTime = JobType == JobType.FixedTime;
        Fold firstHalf = direction == Direction.Forward ? Fold.Earlier : Fold.Later;

        DateTimeOffset inFold = Walk(origin, zone, inclusive, direction, fixedTime);

        if (fixedTime && direction == Direction.Forward)
        {
            // A fixed-time job never runs on the later half of a repeated range.
            return inFold;
        }
        if (!zone.HasTransitions || FoldOf(origin, zone) != firstHalf)
        {
            return inFold;
        }

        DateTimeOffset otherEdge = OtherFoldEdge(origin, firstHalf, zone);
        if (Precedes(direction, inFold, otherEdge))
        {
            return inFold;
        }

        DateTimeOffset inOtherFold = Walk(otherEdge, zone, true, direction, fixedTime);
        return Precedes(direction, inOtherFold, inFold) ? inOtherFold : inFold;
    }

    private DateTimeOffset Walk(DateTimeOffset origin, Zone zone, bool inclusive, Direction direction, bool fixedTime)
    {
        int step = Step(direction);
        DateTime cursor = Truncate(origin.DateTime);
        if (!inclusive)
        {
            cursor = AddCivilSeconds(cursor, step);
        }

        int iterations = 0;
        while (true)
        {
            if (++iterations > MaxSearchIterations)
            {
                throw new CronException(CronErrorKind.SearchLimitExceeded);
            }

            bool changed = FindMatchingDateComponent(ref cursor, direction, DateComponent.Year);
            if (!changed)
            {
                changed = FindMatchingDateComponent(ref cursor, direction, DateComponent.Month);
            }
            if (!changed)
            {
                changed = FindMatchingDateComponent(ref cursor, direction, DateComponent.Day);
            }
            if (changed)
            {
                cursor = WithTime(cursor, direction == Direction.Forward ? 0 : 23, direction == Direction.Forward ? 0 : 59, direction == Direction.Forward ? 0 : 59);
            }

            bool timeAdjusted = FindMatchingGranularComponent(ref cursor, direction, TimeComponent.Hour);
            if (!timeAdjusted)
            {
                timeAdjusted = FindMatchingGranularComponent(ref cursor, direction, TimeComponent.Minute);
            }
            if (!timeAdjusted)
            {
                FindMatchingGranularComponent(ref cursor, direction, TimeComponent.Second);
            }

            Resolution resolution = zone.Resolve(cursor);
            switch (resolution.Kind)
            {
                case ResolutionKind.Single:
                    if (IsCivilMatching(cursor))
                    {
                        return resolution.Earlier;
                    }
                    cursor = AddCivilSeconds(cursor, step);
                    break;

                case ResolutionKind.Ambiguous:
                    // DST overlap (fall back)
                    if (IsCivilMatching(cursor))
                    {
                        // Behind means more than a second past `origin` in the search direction.
                        bool Behind(DateTimeOffset instant)
                        {
                            DateTimeOffset nudged = zone.AddSeconds(instant, step)
                                ?? throw new CronException(CronErrorKind.InvalidTime);
                            return !Precedes(direction, origin, nudged);
                        }

                        if (fixedTime)
                        {
                            // A fixed-time job runs once, at the earlier of the two instants.
                            if (!Behind(resolution.Earlier))
                            {
                                return resolution.Earlier;
                            }
                        }
                        else
                        {
                            // An interval job runs on both instants; take the first, in search order, that is not behind.
                            DateTimeOffset near = direction == Direction.Forward ? resolution.Earlier : resolution.Later;
                            DateTimeOffset far = direction == Direction.Forward ? resolution.Later : resolution.Earlier;
                            if (!Behind(near))
                            {
                                return near;
                            }
                            if (!Behind(far))
                            {
                                return far;
                            }
                        }
                    }
                    cursor = AddCivilSeconds(cursor, step);
                    break;

                case ResolutionKind.Gap:
                    // DST gap (spring forward). Only a wall clock time the pattern schedules is carried past the gap.
                    if (fixedTime && IsCivilMatching(cursor))
                    {
                        DateTime afterGap = cursor;
                        DateTime beforeGap = cursor;
                        int gapAdjust = 0;
                        DateTimeOffset resolvedAfterGap;
                        while (true)
                        {
                            afterGap = AddCivilSeconds(afterGap, 1);
                            if (++gapAdjust > MaxGapSearchSeconds)
                            {
                                throw new CronException(CronErrorKind.SearchLimitExceeded);
                            }
                            Resolution r = zone.Resolve(afterGap);
                            if (r.Kind != ResolutionKind.Gap)
                            {
                                resolvedAfterGap = r.Earlier;
                                break;
                            }
                        }

                        // The fixed hour/minute/second were skipped; only the date fields must still match.
                        if (Pattern.DayMatch(afterGap.Year, afterGap.Month, afterGap.Day, afterGap.DayOfWeek)
                            && Pattern.MonthMatch(afterGap.Month)
                            && Pattern.YearMatch(afterGap.Year))
                        {
                            int cmp = resolvedAfterGap.UtcTicks.CompareTo(origin.UtcTicks);
                            bool matchesDirection = cmp < 0
                                ? direction == Direction.Backward
                                : cmp == 0 ? inclusive : direction == Direction.Forward;
                            if (matchesDirection)
                            {
                                return resolvedAfterGap;
                            }
                        }

                        if (direction == Direction.Backward)
                        {
                            gapAdjust = 0;
                            while (true)
                            {
                                beforeGap = AddCivilSeconds(beforeGap, -1);
                                if (++gapAdjust > MaxGapSearchSeconds)
                                {
                                    throw new CronException(CronErrorKind.SearchLimitExceeded);
                                }
                                if (zone.Resolve(beforeGap).Kind != ResolutionKind.Gap)
                                {
                                    cursor = beforeGap;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            cursor = afterGap;
                        }
                    }
                    else
                    {
                        cursor = AddCivilSeconds(cursor, step);
                    }
                    break;
            }
        }
    }

    private enum DateComponent
    {
        Day,
        Month,
        Year,
    }

    private enum TimeComponent
    {
        Second,
        Minute,
        Hour,
    }

    private static DateTime AddCivilSeconds(DateTime cursor, long seconds)
    {
        long ticks = cursor.Ticks + seconds * TimeSpan.TicksPerSecond;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            // Walking off the calendar means there is nothing more to find.
            throw new CronException(CronErrorKind.SearchLimitExceeded);
        }
        return new DateTime(ticks, DateTimeKind.Unspecified);
    }

    private static DateTime WithTime(DateTime cursor, int hour, int minute, int second) =>
        new(cursor.Year, cursor.Month, cursor.Day, hour, minute, second, DateTimeKind.Unspecified);

    private bool DateMatches(DateTime c, DateComponent component) => component switch
    {
        DateComponent.Year => Pattern.YearMatch(c.Year),
        DateComponent.Month => Pattern.MonthMatch(c.Month),
        _ => Pattern.DayMatch(c.Year, c.Month, c.Day, c.DayOfWeek),
    };

    private bool FindMatchingDateComponent(ref DateTime cursor, Direction direction, DateComponent component)
    {
        bool changed = false;
        while (!DateMatches(cursor, component))
        {
            AdjustDateComponent(ref cursor, component, direction);
            changed = true;
        }
        return changed;
    }

    private static void CheckYearLimits(DateTime cursor, Direction direction)
    {
        if (direction == Direction.Forward ? cursor.Year >= MaxYear : cursor.Year <= MinYear)
        {
            throw new CronException(CronErrorKind.SearchLimitExceeded);
        }
    }

    /// <summary>Steps a date component to the far edge of the neighbouring unit.</summary>
    private static void AdjustDateComponent(ref DateTime cursor, DateComponent component, Direction direction)
    {
        CheckYearLimits(cursor, direction);
        bool forward = direction == Direction.Forward;
        cursor = component switch
        {
            DateComponent.Year => forward
                ? new DateTime(cursor.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
                : new DateTime(cursor.Year - 1, 12, 31, 23, 59, 59, DateTimeKind.Unspecified),
            DateComponent.Month => forward
                ? new DateTime(cursor.Year, cursor.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(1)
                : new DateTime(cursor.Year, cursor.Month, 1, 23, 59, 59, DateTimeKind.Unspecified).AddDays(-1),
            _ => forward
                ? WithTime(cursor.AddDays(1), 0, 0, 0)
                : WithTime(cursor.AddDays(-1), 23, 59, 59),
        };
    }

    private bool FindMatchingGranularComponent(ref DateTime cursor, Direction direction, TimeComponent component)
    {
        (int current, CronComponent field) = component switch
        {
            TimeComponent.Hour => (cursor.Hour, Pattern.Hours),
            TimeComponent.Minute => (cursor.Minute, Pattern.Minutes),
            _ => (cursor.Second, Pattern.Seconds),
        };

        int match = CronPattern.FindMatchInComponent(field, current, direction == Direction.Forward);
        if (match >= 0)
        {
            if (match != current)
            {
                // Set the component and reset the lower ones.
                bool forward = direction == Direction.Forward;
                cursor = component switch
                {
                    TimeComponent.Hour => WithTime(cursor, match, forward ? 0 : 59, forward ? 0 : 59),
                    TimeComponent.Minute => WithTime(cursor, cursor.Hour, match, forward ? 0 : 59),
                    _ => WithTime(cursor, cursor.Hour, cursor.Minute, match),
                };
            }
            return false;
        }

        // No match left in this unit: step the next larger one.
        if (component == TimeComponent.Hour)
        {
            AdjustDateComponent(ref cursor, DateComponent.Day, direction);
        }
        else
        {
            CheckYearLimits(cursor, direction);
            bool forward = direction == Direction.Forward;
            long stepSeconds = component == TimeComponent.Minute ? 3600 : 60;
            DateTime stepped = AddCivilSeconds(cursor, stepSeconds * Step(direction));
            cursor = component == TimeComponent.Minute
                ? WithTime(stepped, stepped.Hour, forward ? 0 : 59, forward ? 0 : 59)
                : WithTime(stepped, stepped.Hour, stepped.Minute, forward ? 0 : 59);
        }
        return true;
    }
}
