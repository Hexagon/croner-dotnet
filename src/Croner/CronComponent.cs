using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Croner;

/// <summary>
/// Bit flags stored per value of a <see cref="CronComponent"/>.
/// </summary>
internal static class CronBits
{
    public const byte None = 0;
    public const byte All = 1;

    // Used for nth weekday
    public const byte Nth1st = 1 << 1;
    public const byte Nth2nd = 1 << 2;
    public const byte Nth3rd = 1 << 3;
    public const byte Nth4th = 1 << 4;
    public const byte Nth5th = 1 << 5;
    public const byte NthAll = Nth1st | Nth2nd | Nth3rd | Nth4th | Nth5th;

    // Used for last day of month / last weekday of month
    public const byte Last = 1 << 6;

    // Used for closest weekday
    public const byte ClosestWeekday = 1 << 7;

    public static readonly byte[] NthBits = { Nth1st, Nth2nd, Nth3rd, Nth4th, Nth5th };
}

/// <summary>
/// One field of a cron pattern, such as minutes, hours or day of week.
/// </summary>
/// <remarks>
/// Each value the field can take holds a set of bits: a plain match, and for
/// day-of-month and day-of-week the <c>L</c>, <c>W</c> and <c>#</c> modifiers.
/// </remarks>
public sealed class CronComponent : IEquatable<CronComponent>
{
    private readonly byte[] _bitfields;
    private readonly byte _features;
    private byte _enabledFeatures;
    private readonly ushort _inputOffset;
    private bool _lenientSteps;
    private bool _wrapRanges;

    internal CronComponent(ushort min, ushort max, byte features, ushort inputOffset)
    {
        _bitfields = new byte[max > 0 ? max + 1 : 0];
        Min = min;
        Max = max;
        // ALL and LAST are always allowed
        _features = (byte)(features | CronBits.All | CronBits.Last);
        _inputOffset = inputOffset;
    }

    /// <summary>The smallest value this field can take.</summary>
    public int Min { get; }

    /// <summary>The largest value this field can take.</summary>
    public int Max { get; }

    /// <summary>The step of the last stepped expression in this field, <c>1</c> when there was none.</summary>
    public int Step { get; private set; } = 1;

    /// <summary>Whether the field was written as, or stepped from, a wildcard (<c>*</c>).</summary>
    public bool FromWildcard { get; private set; }

    internal void SetLenientSteps(bool lenient) => _lenientSteps = lenient;

    /// <summary>
    /// Lets a range whose end is before its start wrap around past the maximum, as Quartz does for
    /// day-of-week ranges like <c>FRI-MON</c> or <c>SAT-SUN</c> (<c>7-1</c>).
    /// </summary>
    internal void SetWrapRanges(bool wrap) => _wrapRanges = wrap;

    /// <summary>Whether <c>start-end</c> with <c>start &gt; end</c> wraps around instead of failing.</summary>
    private bool Wraps(int start, int end) =>
        _wrapRanges && start > end && start <= Max && end >= Min + _inputOffset;

    /// <summary>Sets <paramref name="bit"/> on every <paramref name="step"/>th value from <paramref name="start"/> to <paramref name="end"/>, wrapping around.</summary>
    private void SetWrappedRange(int start, int end, int step, byte bit)
    {
        int low = Min + _inputOffset;
        int span = Max - low + 1;
        for (int value = start; value <= end + span; value += step)
        {
            SetBit((ushort)(value > Max ? value - span : value), bit);
        }
    }

    internal void SetBit(ushort pos, byte bit)
    {
        int index = CheckedIndex(pos, bit, applyOffset: true);
        _bitfields[index] |= bit;
    }

    internal void UnsetBit(ushort pos, byte bit)
    {
        int index = CheckedIndex(pos, bit, applyOffset: true);
        _bitfields[index] &= (byte)~bit;
    }

    private int CheckedIndex(ushort pos, byte bit, bool applyOffset)
    {
        if (applyOffset)
        {
            if (pos < _inputOffset)
            {
                throw new CronException(CronErrorKind.InvalidField,
                    $"Position {pos} is less than the input offset {_inputOffset}.");
            }
            pos -= _inputOffset;
        }
        if (pos < Min || pos > Max)
        {
            throw new CronException(CronErrorKind.InvalidField,
                $"Position {pos} is out of bounds for the current range ({Min}-{Max}).");
        }
        if ((_features & bit) != bit)
        {
            throw new CronException(CronErrorKind.InvalidField,
                $"Bit 0b{Convert.ToString(bit, 2).PadLeft(8, '0')} is not supported by the current features 0b{Convert.ToString(_features, 2).PadLeft(8, '0')}.");
        }
        if (pos >= _bitfields.Length)
        {
            throw new CronException(CronErrorKind.InvalidField,
                $"Position {pos} is out of the bitfields vector's bounds.");
        }
        return pos;
    }

    /// <summary>
    /// Checks whether a bit is set for a value. Values are in the internal numbering,
    /// where weekdays use <c>0</c> for Sunday.
    /// </summary>
    internal bool IsBitSet(int pos, byte bit)
    {
        if (pos < Min || pos > Max)
        {
            throw new CronException(CronErrorKind.InvalidField,
                $"Position {pos} is out of bounds for the current range ({Min}-{Max}).");
        }
        if ((_features & bit) != bit)
        {
            throw new CronException(CronErrorKind.InvalidField,
                $"Bit 0b{Convert.ToString(bit, 2).PadLeft(8, '0')} is not supported by the current features 0b{Convert.ToString(_features, 2).PadLeft(8, '0')}.");
        }
        return pos < _bitfields.Length && (_bitfields[pos] & bit) != 0;
    }

    /// <summary>Fast path used by the matcher: assumes <paramref name="pos"/> is in range.</summary>
    internal bool Has(int pos, byte bit) => (_bitfields[pos] & bit) != 0;

    internal void EnableFeature(byte feature)
    {
        if (!IsFeatureAllowed(feature))
        {
            throw new CronException(CronErrorKind.InvalidField,
                $"Feature 0b{Convert.ToString(feature, 2).PadLeft(8, '0')} is not supported by the current features 0b{Convert.ToString(_features, 2).PadLeft(8, '0')}.");
        }
        _enabledFeatures |= feature;
    }

    internal bool IsFeatureAllowed(byte feature) => (_features & feature) == feature;

    internal bool IsFeatureEnabled(byte feature) => (_enabledFeatures & feature) == feature;

    /// <summary>
    /// The values this field matches without a modifier, in ascending order.
    /// </summary>
    /// <remarks>
    /// For an hour field of <c>1-5,10</c> this is <c>[1, 2, 3, 4, 5, 10]</c>. Values selected only
    /// through <c>L</c>, <c>W</c> or <c>#</c> are not included; <see cref="HasModifiers"/> reports those.
    /// Weekdays use <c>0</c> for Sunday, whichever weekday numbering the pattern was parsed with.
    /// </remarks>
    public IReadOnlyList<int> GetValues() => GetSetValues(CronBits.All);

    /// <summary>Whether the field uses <c>L</c>, <c>W</c> or <c>#</c>, whose matches depend on the month.</summary>
    public bool HasModifiers =>
        _enabledFeatures != CronBits.None || _bitfields.Any(b => (b & ~CronBits.All) != CronBits.None);

    internal List<int> GetSetValues(byte bit)
    {
        var result = new List<int>();
        for (int i = Min; i <= Max; i++)
        {
            if (i < _bitfields.Length && (_bitfields[i] & bit) != 0)
            {
                result.Add(i);
            }
        }
        return result;
    }

    internal int CountSetValues(byte bit) => GetSetValues(bit).Count;

    /// <summary>
    /// Parses one field of a cron expression and sets the corresponding bits.
    /// </summary>
    internal void Parse(string field)
    {
        if (field == "*")
        {
            // `*` is the whole field, so nothing else is set yet.
            FromWildcard = true;
            _bitfields.AsSpan(Min, Max - Min + 1).Fill(CronBits.All);
            return;
        }

        foreach (string part in field.Split(','))
        {
            string parsedPart = part.Trim();
            if (parsedPart.Length == 0)
            {
                throw new CronException(CronErrorKind.InvalidField, $"Empty value in the list '{field}'.");
            }

            if (parsedPart.Contains('/'))
            {
                HandleStepping(parsedPart);
            }
            else if (parsedPart.Contains('-'))
            {
                HandleRange(parsedPart);
            }
            else if (parsedPart.Contains('W'))
            {
                HandleClosestWeekday(parsedPart);
            }
            else if (string.Equals(parsedPart, "L", StringComparison.OrdinalIgnoreCase))
            {
                if (IsFeatureAllowed(CronBits.NthAll))
                {
                    // In the day-of-week field, L only means something together with a weekday.
                    throw new CronException(CronErrorKind.InvalidField,
                        "L in the day-of-week field needs a weekday, as in 5L for the last Friday of the month.");
                }
                EnableFeature(CronBits.Last);
            }
            else
            {
                if (parsedPart.Contains('#') && !IsFeatureAllowed(CronBits.NthAll))
                {
                    throw new CronException(CronErrorKind.InvalidField,
                        "Nth specifier # not allowed in the current field.");
                }

                if (parsedPart.Contains('L') && !IsFeatureAllowed(CronBits.NthAll))
                {
                    throw new CronException(CronErrorKind.InvalidField, "L not allowed in the current field.");
                }

                HandleNumber(parsedPart);
            }
        }
    }

    private static bool TryParseUShort(string s, out ushort value) =>
        ushort.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static ushort ParseUShort(string s, string error)
    {
        if (!TryParseUShort(s, out ushort value))
        {
            throw new CronException(CronErrorKind.InvalidField, error);
        }
        return value;
    }

    /// <summary>
    /// The bit a day-of-week value sets: <c>#n</c> for the nth weekday of the month, <c>#L</c> or a trailing
    /// <c>L</c> for the last, and a plain match otherwise.
    /// </summary>
    private byte GetNthBit(string value)
    {
        byte bit = ReadNthBit(value);
        if (bit != CronBits.All && !IsFeatureAllowed(CronBits.NthAll))
        {
            // Only the day-of-week field has nth and last weekdays; L in day-of-month has to stand alone.
            throw new CronException(CronErrorKind.InvalidField,
                $"Invalid value '{value}': L and LW have to be values of their own in the day-of-month field.");
        }
        return bit;
    }

    private static byte ReadNthBit(string value)
    {
        int nthPos = value.IndexOf('#');
        if (nthPos < 0)
        {
            return value.EndsWith('L') ? CronBits.Last : CronBits.All;
        }
        ReadOnlySpan<char> nthPart = value.AsSpan(nthPos + 1);
        if (nthPart.SequenceEqual("L"))
        {
            return CronBits.Last;
        }
        if (!byte.TryParse(nthPart, NumberStyles.None, CultureInfo.InvariantCulture, out byte nth))
        {
            throw new CronException(CronErrorKind.InvalidField, $"Invalid nth specifier in '{value}'.");
        }
        if (nth == 0 || nth > 5)
        {
            throw new CronException(CronErrorKind.InvalidField, $"Nth specifier out of bounds in '{value}'.");
        }
        return CronBits.NthBits[nth - 1];
    }

    /// <summary>Removes the <c>#n</c>, <c>#L</c> or trailing <c>L</c> that <see cref="GetNthBit"/> reads.</summary>
    private static string StripNthPart(string value)
    {
        int idx = value.IndexOf('#');
        if (idx >= 0)
        {
            return value.Substring(0, idx);
        }
        return value.EndsWith('L') ? value.Substring(0, value.Length - 1) : value;
    }

    private void HandleClosestWeekday(string value)
    {
        int dayPos = value.IndexOf('W');
        if (dayPos < 0)
        {
            HandleNumber(value);
            return;
        }
        if (dayPos != value.Length - 1)
        {
            throw new CronException(CronErrorKind.InvalidField,
                $"Invalid value '{value}': W has to follow a day, as in 15W, or L, as in LW.");
        }
        string dayStr = value.Substring(0, dayPos);

        // "LW" - last weekday of the month. The closest-weekday feature on its own means LW, kept apart from
        // the last-day feature, so "L,LW" keeps both meanings.
        if (string.Equals(dayStr, "L", StringComparison.OrdinalIgnoreCase))
        {
            EnableFeature(CronBits.ClosestWeekday);
            return;
        }

        ushort day = ParseUShort(dayStr, "Invalid day for closest weekday.");
        if (day < Min || day > Max)
        {
            throw new CronException(CronErrorKind.InvalidField, "Day for closest weekday out of bounds.");
        }
        SetBit(day, CronBits.ClosestWeekday);
    }

    private void HandleRange(string range)
    {
        byte bitToSet = GetNthBit(range);
        string clean = StripNthPart(range);

        string[] parts = clean.Split('-');
        if (parts.Length != 2)
        {
            throw new CronException(CronErrorKind.InvalidField, "Invalid range syntax.");
        }

        ushort start = ParseUShort(parts[0].Trim(), "Invalid start of range.");
        ushort end = ParseUShort(parts[1].Trim(), "Invalid end of range.");

        if (Wraps(start, end))
        {
            SetWrappedRange(start, end, 1, bitToSet);
            return;
        }

        if (start > end || start < Min || end > Max)
        {
            throw new CronException(CronErrorKind.InvalidField, "Range out of bounds.");
        }

        for (int value = start; value <= end; value++)
        {
            SetBit((ushort)value, bitToSet);
        }
    }

    private void HandleNumber(string value)
    {
        byte bitToSet = GetNthBit(value);
        string clean = StripNthPart(value);
        ushort num = ParseUShort(clean, "Invalid number.");
        if (num < Min || num > Max)
        {
            throw new CronException(CronErrorKind.InvalidField, "Number out of bounds.");
        }
        SetBit(num, bitToSet);
    }

    private void HandleStepping(string steppedRange)
    {
        byte bitToSet = GetNthBit(steppedRange);
        string clean = StripNthPart(steppedRange);

        string[] parts = clean.Split('/');
        if (parts.Length != 2)
        {
            throw new CronException(CronErrorKind.InvalidField, "Invalid stepped range syntax.");
        }

        string rangePart = parts[0];
        string stepStr = parts[1];
        ushort step = ParseUShort(stepStr, "Invalid step.");

        Step = step;

        if (step == 0)
        {
            throw new CronException(CronErrorKind.InvalidField, "Step cannot be zero.");
        }

        int start, end;
        if (rangePart == "*")
        {
            FromWildcard = true;
            start = Min + _inputOffset;
            end = Max;
        }
        else if (rangePart.Contains('-'))
        {
            string[] bounds = rangePart.Split('-');
            if (bounds.Length != 2)
            {
                throw new CronException(CronErrorKind.InvalidField, "Invalid range syntax in stepping.");
            }
            start = ParseUShort(bounds[0], "Invalid range start.");
            end = ParseUShort(bounds[1], "Invalid range end.");
        }
        else if (rangePart.Length == 0)
        {
            if (_lenientSteps)
            {
                // In sloppy mode, treat /10 as */10
                FromWildcard = true;
                start = Min + _inputOffset;
                end = Max;
            }
            else
            {
                throw new CronException(CronErrorKind.InvalidField,
                    $"Invalid step syntax: omitting the starting point like \"/{stepStr}\" is not allowed. Use \"*/{stepStr}\" for wildcard steps or \"X-Y/{stepStr}\" for range steps.");
            }
        }
        else if (_lenientSteps)
        {
            // In sloppy mode, allow a single number as the starting point
            start = ParseUShort(rangePart, "Invalid start.");
            end = Max;
        }
        else
        {
            throw new CronException(CronErrorKind.InvalidField,
                $"Invalid step syntax: single number steps like \"{rangePart}/{stepStr}\" are not allowed. Use \"*/{stepStr}\" for wildcard steps or \"X-Y/{stepStr}\" for range steps.");
        }

        if (Wraps(start, end))
        {
            SetWrappedRange(start, end, step, bitToSet);
            return;
        }

        if (start < Min || end > Max || start > end)
        {
            throw new CronException(CronErrorKind.InvalidField, "Range is out of bounds in stepping.");
        }

        for (int value = start; value <= end; value += step)
        {
            SetBit((ushort)value, bitToSet);
        }
    }

    /// <inheritdoc />
    public bool Equals(CronComponent? other)
    {
        if (other is null)
        {
            return false;
        }
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        return Min == other.Min
            && Max == other.Max
            && Step == other.Step
            && FromWildcard == other.FromWildcard
            && _features == other._features
            && _enabledFeatures == other._enabledFeatures
            && _bitfields.AsSpan().SequenceEqual(other._bitfields);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CronComponent);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Min);
        hash.Add(Max);
        hash.Add(Step);
        hash.Add(FromWildcard);
        hash.Add(_enabledFeatures);
        hash.AddBytes(_bitfields);
        return hash.ToHashCode();
    }
}
