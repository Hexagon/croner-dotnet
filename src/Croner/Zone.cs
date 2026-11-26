using System;
using System.Runtime.CompilerServices;

namespace Croner;

internal enum ResolutionKind
{
    Single,
    Ambiguous,
    Gap,
}

/// <summary>
/// The instants a wall clock time maps to in a zone: one, two (when the clock goes back),
/// or none (when the clock jumps forward).
/// </summary>
internal readonly struct Resolution
{
    public Resolution(ResolutionKind kind, DateTimeOffset earlier = default, DateTimeOffset later = default)
    {
        Kind = kind;
        Earlier = earlier;
        Later = later;
    }

    public ResolutionKind Kind { get; }

    /// <summary>The single instant, or the earlier of two.</summary>
    public DateTimeOffset Earlier { get; }

    /// <summary>The later of two instants.</summary>
    public DateTimeOffset Later { get; }
}

/// <summary>
/// The time zone a search runs in: either a <see cref="TimeZoneInfo"/> or a fixed UTC offset.
/// All instants handled by the search are kept as <see cref="DateTimeOffset"/> values carrying
/// the zone's offset at that instant, so their wall clock time is <see cref="DateTimeOffset.DateTime"/>.
/// </summary>
internal sealed class Zone
{
    private readonly TimeZoneInfo? _tz;
    private readonly TimeSpan _offset;

    private Zone(TimeZoneInfo? tz, TimeSpan offset)
    {
        _tz = tz;
        _offset = offset;
        HasTransitions = tz is not null && tz.GetAdjustmentRules().Length > 0;
    }

    public static readonly Zone Utc = new(null, TimeSpan.Zero);

    // GetAdjustmentRules() copies every rule of the zone, so a zone is set up once per TimeZoneInfo instance.
    private static readonly ConditionalWeakTable<TimeZoneInfo, Zone> Zones = new();

    public static Zone Of(TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        return tz == TimeZoneInfo.Utc ? Utc : Zones.GetValue(tz, static z => new Zone(z, TimeSpan.Zero));
    }

    public static Zone Fixed(TimeSpan offset) => offset == TimeSpan.Zero ? Utc : new Zone(null, offset);

    /// <summary>Whether the zone's offset can change, so that wall clock times can repeat or be skipped.</summary>
    public bool HasTransitions { get; }

    /// <summary>Expresses an instant with this zone's offset.</summary>
    public DateTimeOffset ToZone(DateTimeOffset value) =>
        _tz is null ? value.ToOffset(_offset) : TimeZoneInfo.ConvertTime(value, _tz);

    /// <summary>Adds seconds to an instant (absolute time), or returns null on overflow.</summary>
    public DateTimeOffset? AddSeconds(DateTimeOffset value, long seconds)
    {
        try
        {
            return ToZone(value.AddSeconds(seconds));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // Real UTC offsets lie within ±14 hours, so every instant a wall clock time can denote lies within 14 hours of it.
    private static readonly long MaxOffsetTicks = TimeSpan.FromHours(14).Ticks;

    /// <summary>Maps a wall clock time (Kind = Unspecified) to the instants it denotes.</summary>
    /// <remarks>
    /// Only the zone's UTC to local conversion is used. <see cref="TimeZoneInfo.IsInvalidTime(DateTime)"/>,
    /// <see cref="TimeZoneInfo.IsAmbiguousTime(DateTime)"/> and the local-time overload of <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>
    /// do not see transitions that change the base offset, such as the day <c>Pacific/Apia</c> skipped on
    /// 2011-12-30. A wall clock time denotes an instant when one of the offsets in effect around it, applied to
    /// it, gives an instant that has that same offset.
    /// </remarks>
    public Resolution Resolve(DateTime civil)
    {
        if (_tz is null)
        {
            try
            {
                return new Resolution(ResolutionKind.Single, new DateTimeOffset(civil, _offset));
            }
            catch (ArgumentException)
            {
                // The UTC instant of a wall clock time at the edge of DateTime's range
                throw new CronException(CronErrorKind.InvalidTime);
            }
        }

        long ticks = civil.Ticks;
        TimeSpan before = OffsetAt(ticks - MaxOffsetTicks);
        TimeSpan after = OffsetAt(ticks + MaxOffsetTicks);
        bool beforeFits = Fits(ticks, before);
        bool afterFits = before != after && Fits(ticks, after);

        if (beforeFits && afterFits)
        {
            // The larger offset belongs to the earlier instant.
            TimeSpan max = before > after ? before : after;
            TimeSpan min = before > after ? after : before;
            return new Resolution(ResolutionKind.Ambiguous, new DateTimeOffset(civil, max), new DateTimeOffset(civil, min));
        }
        if (beforeFits || afterFits)
        {
            return new Resolution(ResolutionKind.Single, new DateTimeOffset(civil, beforeFits ? before : after));
        }
        return new Resolution(ResolutionKind.Gap);
    }

    /// <summary>Whether the instant <paramref name="ticks"/> - <paramref name="offset"/> has that offset.</summary>
    private bool Fits(long ticks, TimeSpan offset)
    {
        long utc = ticks - offset.Ticks;
        if (utc < DateTime.MinValue.Ticks || utc > DateTime.MaxValue.Ticks)
        {
            // The UTC instant of a wall clock time at the edge of DateTime's range
            throw new CronException(CronErrorKind.InvalidTime);
        }
        return OffsetAt(utc) == offset;
    }

    /// <summary>The zone's UTC offset at a UTC instant, clamped to the range of <see cref="DateTime"/>.</summary>
    private TimeSpan OffsetAt(long utcTicks) =>
        _tz!.GetUtcOffset(new DateTime(Math.Clamp(utcTicks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), DateTimeKind.Utc));
}
