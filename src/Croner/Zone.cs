using System;

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

    public static Zone Of(TimeZoneInfo tz) =>
        tz == TimeZoneInfo.Utc ? Utc : new Zone(tz ?? throw new ArgumentNullException(nameof(tz)), TimeSpan.Zero);

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

    /// <summary>Maps a wall clock time (Kind = Unspecified) to the instants it denotes.</summary>
    public Resolution Resolve(DateTime civil)
    {
        try
        {
            if (_tz is null)
            {
                return new Resolution(ResolutionKind.Single, new DateTimeOffset(civil, _offset));
            }
            if (_tz.IsInvalidTime(civil))
            {
                return new Resolution(ResolutionKind.Gap);
            }
            if (_tz.IsAmbiguousTime(civil))
            {
                TimeSpan[] offsets = _tz.GetAmbiguousTimeOffsets(civil);
                TimeSpan max = offsets[0], min = offsets[0];
                foreach (TimeSpan o in offsets)
                {
                    if (o > max) max = o;
                    if (o < min) min = o;
                }
                // The larger offset belongs to the earlier instant.
                return new Resolution(ResolutionKind.Ambiguous,
                    new DateTimeOffset(civil, max), new DateTimeOffset(civil, min));
            }
            return new Resolution(ResolutionKind.Single, new DateTimeOffset(civil, _tz.GetUtcOffset(civil)));
        }
        catch (ArgumentException)
        {
            // The UTC instant of a wall clock time at the edge of DateTime's range
            throw new CronException(CronErrorKind.InvalidTime);
        }
    }
}
