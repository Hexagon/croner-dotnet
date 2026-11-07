using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Croner;

/// <summary>The direction of a time search.</summary>
public enum Direction
{
    /// <summary>Towards the future.</summary>
    Forward,

    /// <summary>Towards the past.</summary>
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
/// A parsed cron schedule. Use it to check whether a time matches, find next and previous
/// occurrences, enumerate occurrences, and describe the schedule in plain language.
/// </summary>
/// <example>
/// <code>
/// var cron = Cron.Parse("0 0 * * FRI");
/// DateTimeOffset next = cron.FindNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
/// </code>
/// </example>
public sealed class Cron : IEquatable<Cron>
{
    /// <summary>
    /// The latest year a search reaches. Prevents endless searches for patterns that never match.
    /// </summary>
    public const int YearUpperLimit = 5000;

    /// <summary>The earliest year a search reaches (1 AD).</summary>
    public const int YearLowerLimit = 1;

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

    /// <summary>Parses a cron pattern with the given options, or the defaults.</summary>
    /// <exception cref="CronException">The pattern is invalid.</exception>
    public static Cron Parse(string pattern, CronParserOptions? options = null) =>
        new CronParser(options).Parse(pattern);

    /// <summary>Tries to parse a cron pattern with the default options. Returns <c>false</c> when it is <c>null</c> or invalid.</summary>
    public static bool TryParse([NotNullWhen(true)] string? pattern, [NotNullWhen(true)] out Cron? cron) =>
        new CronParser().TryParse(pattern, out cron);

    /// <summary>Tries to parse a cron pattern with the given options. Returns <c>false</c> when it is <c>null</c> or invalid.</summary>
    public static bool TryParse([NotNullWhen(true)] string? pattern, CronParserOptions? options, [NotNullWhen(true)] out Cron? cron) =>
        new CronParser(options).TryParse(pattern, out cron);

    // ------------------------------------------------------------------
    // Matching
    // ------------------------------------------------------------------

    /// <summary>
    /// Checks whether the wall clock time of <paramref name="time"/> (in its own offset) matches the pattern.
    /// Sub-second precision is ignored.
    /// </summary>
    public bool IsTimeMatching(DateTimeOffset time) => IsCivilMatching(Truncate(time.DateTime));

    /// <summary>
    /// Checks whether <paramref name="time"/>, as wall clock time in <paramref name="zone"/>, matches the pattern.
    /// </summary>
    public bool IsTimeMatching(DateTimeOffset time, TimeZoneInfo zone) =>
        IsCivilMatching(Truncate(TimeZoneInfo.ConvertTime(time, zone).DateTime));

    /// <summary>
    /// Checks whether the wall clock fields of <paramref name="time"/> match the pattern,
    /// regardless of its <see cref="DateTime.Kind"/>.
    /// </summary>
    public bool IsTimeMatching(DateTime time) => IsCivilMatching(Truncate(time));

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
    /// Finds the next occurrence after <paramref name="start"/>, evaluated in the fixed UTC offset of <paramref name="start"/>.
    /// </summary>
    /// <param name="start">Where to start searching.</param>
    /// <param name="inclusive">Whether <paramref name="start"/> itself (truncated to the second) may be returned.</param>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTimeOffset FindNextOccurrence(DateTimeOffset start, bool inclusive = false) =>
        FindOccurrence(start, Zone.Fixed(start.Offset), inclusive, Direction.Forward);

    /// <summary>
    /// Finds the next occurrence after <paramref name="start"/>, evaluated as wall clock time in
    /// <paramref name="zone"/>, with correct handling of daylight saving time transitions.
    /// The result carries the zone's UTC offset at that instant.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTimeOffset FindNextOccurrence(DateTimeOffset start, TimeZoneInfo zone, bool inclusive = false) =>
        FindOccurrence(start, Zone.Of(zone), inclusive, Direction.Forward);

    /// <summary>
    /// Finds the next occurrence after <paramref name="start"/>. A <see cref="DateTimeKind.Utc"/> value is evaluated in UTC,
    /// a <see cref="DateTimeKind.Local"/> value in <see cref="TimeZoneInfo.Local"/>, and an
    /// <see cref="DateTimeKind.Unspecified"/> value as a plain wall clock without time zone. The result has the same kind.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTime FindNextOccurrence(DateTime start, bool inclusive = false) =>
        FromKind(FindOccurrence(ToOffset(start, out Zone zone), zone, inclusive, Direction.Forward), start.Kind);

    /// <summary>
    /// Finds the previous occurrence before <paramref name="start"/>, evaluated in the fixed UTC offset of <paramref name="start"/>.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTimeOffset FindPreviousOccurrence(DateTimeOffset start, bool inclusive = false) =>
        FindOccurrence(start, Zone.Fixed(start.Offset), inclusive, Direction.Backward);

    /// <summary>
    /// Finds the previous occurrence before <paramref name="start"/>, evaluated as wall clock time in <paramref name="zone"/>.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTimeOffset FindPreviousOccurrence(DateTimeOffset start, TimeZoneInfo zone, bool inclusive = false) =>
        FindOccurrence(start, Zone.Of(zone), inclusive, Direction.Backward);

    /// <summary>
    /// Finds the previous occurrence before <paramref name="start"/>. See <see cref="FindNextOccurrence(DateTime, bool)"/>
    /// for how the <see cref="DateTime.Kind"/> is interpreted.
    /// </summary>
    /// <exception cref="CronException">No occurrence exists within the search limits.</exception>
    public DateTime FindPreviousOccurrence(DateTime start, bool inclusive = false) =>
        FromKind(FindOccurrence(ToOffset(start, out Zone zone), zone, inclusive, Direction.Backward), start.Kind);

    /// <summary>
    /// Tries to find the next (or previous) occurrence. Returns <c>false</c> when none exists within the search limits.
    /// </summary>
    /// <param name="start">Where to start searching.</param>
    /// <param name="zone">The time zone to evaluate in, or <c>null</c> for the fixed offset of <paramref name="start"/>.</param>
    /// <param name="inclusive">Whether <paramref name="start"/> itself may be returned.</param>
    /// <param name="direction">Search direction.</param>
    /// <param name="result">The occurrence found.</param>
    public bool TryFindOccurrence(DateTimeOffset start, TimeZoneInfo? zone, bool inclusive, Direction direction, out DateTimeOffset result)
    {
        try
        {
            result = FindOccurrence(start, zone is null ? Zone.Fixed(start.Offset) : Zone.Of(zone), inclusive, direction);
            return true;
        }
        catch (CronException)
        {
            result = default;
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Iteration
    // ------------------------------------------------------------------

    /// <summary>
    /// Enumerates occurrences after <paramref name="start"/> (exclusive), towards the future.
    /// </summary>
    /// <param name="start">Where to start.</param>
    /// <param name="zone">The time zone to evaluate in, or <c>null</c> for the fixed offset of <paramref name="start"/>.</param>
    public IEnumerable<DateTimeOffset> IterAfter(DateTimeOffset start, TimeZoneInfo? zone = null) =>
        Iterate(start, ZoneFor(start, zone), inclusive: false, Direction.Forward);

    /// <summary>
    /// Enumerates occurrences before <paramref name="start"/> (exclusive), towards the past.
    /// </summary>
    public IEnumerable<DateTimeOffset> IterBefore(DateTimeOffset start, TimeZoneInfo? zone = null) =>
        Iterate(start, ZoneFor(start, zone), inclusive: false, Direction.Backward);

    /// <summary>
    /// Enumerates occurrences from <paramref name="start"/> (inclusive) in the given direction.
    /// </summary>
    public IEnumerable<DateTimeOffset> IterFrom(DateTimeOffset start, Direction direction, TimeZoneInfo? zone = null) =>
        Iterate(start, ZoneFor(start, zone), inclusive: true, direction);

    /// <summary>Enumerates occurrences after <paramref name="start"/> (exclusive). See <see cref="FindNextOccurrence(DateTime, bool)"/> for kind handling.</summary>
    public IEnumerable<DateTime> IterAfter(DateTime start) => IterateKind(start, false, Direction.Forward);

    /// <summary>Enumerates occurrences before <paramref name="start"/> (exclusive). See <see cref="FindNextOccurrence(DateTime, bool)"/> for kind handling.</summary>
    public IEnumerable<DateTime> IterBefore(DateTime start) => IterateKind(start, false, Direction.Backward);

    /// <summary>Enumerates occurrences from <paramref name="start"/> (inclusive). See <see cref="FindNextOccurrence(DateTime, bool)"/> for kind handling.</summary>
    public IEnumerable<DateTime> IterFrom(DateTime start, Direction direction) => IterateKind(start, true, direction);

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
            throw new CronException(CronErrorKind.TimeSearchLimitExceeded);
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
                throw new CronException(CronErrorKind.TimeSearchLimitExceeded);
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
                                throw new CronException(CronErrorKind.TimeSearchLimitExceeded);
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
                                    throw new CronException(CronErrorKind.TimeSearchLimitExceeded);
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
            throw new CronException(CronErrorKind.TimeSearchLimitExceeded);
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
        if (direction == Direction.Forward ? cursor.Year >= YearUpperLimit : cursor.Year <= YearLowerLimit)
        {
            throw new CronException(CronErrorKind.TimeSearchLimitExceeded);
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
