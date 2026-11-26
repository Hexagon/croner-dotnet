using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Croner;

/// <summary>How the parser treats the seconds field.</summary>
public enum SecondsMode
{
    /// <summary>Patterns may have a seconds field; 5-field patterns default seconds to <c>0</c>.</summary>
    Optional,

    /// <summary>Patterns must have a seconds field.</summary>
    Required,

    /// <summary>Patterns must not have a seconds field.</summary>
    Disallowed,
}

/// <summary>How the parser treats the year field.</summary>
public enum YearMode
{
    /// <summary>Patterns may have a year field (7th field); when omitted it defaults to <c>*</c>.</summary>
    Optional,

    /// <summary>Patterns must have a year field.</summary>
    Required,

    /// <summary>Patterns must not have a year field.</summary>
    Disallowed,
}

/// <summary>
/// Which patterns count as fixed-time jobs in daylight saving time (DST) handling. See <see cref="JobType"/>.
/// </summary>
/// <remarks>
/// A fixed-time job runs once when its wall clock time happens twice (fall back), and runs at the first
/// valid time after a gap (spring forward). Any other job is an interval job, which runs on both passes of
/// a repeated time and skips times in a gap. Matches the <c>fixedTimeRule</c> option of croner for
/// JavaScript and croner-rust.
/// </remarks>
public enum FixedTimeRule
{
    /// <summary>
    /// The default. Seconds, minutes and hours each hold a single value, such as <c>0 30 2 * * *</c>.
    /// A list or range, such as <c>0 30 1,13 * * *</c>, makes an interval job.
    /// </summary>
    SingleValue,

    /// <summary>
    /// Seconds, minutes and hours hold no wildcard (<c>*</c>) and no step (<c>/</c>), so lists and ranges
    /// such as <c>0 30 2,14 * * *</c> count as fixed-time too. This is closer to Vixie cron. Unlike Vixie
    /// cron, a step without <c>*</c> (for example <c>0 0-23/2 * * *</c>) still makes an interval job.
    /// </summary>
    NoWildcard,

    /// <summary>
    /// Every pattern is a fixed-time job, including <c>*/30 * * * *</c>: a repeated hour runs once, on its
    /// first pass, and times in a gap run once at its end. This is the DST behaviour of croner 2.x, kept for
    /// schedules that depend on it. A forward search still never returns a time before its start.
    /// Matches croner-rust; croner for JavaScript has no such value.
    /// </summary>
    All,
}

/// <summary>Options that control how cron patterns are parsed.</summary>
public sealed record CronParserOptions
{
    /// <summary>The default options: optional seconds and year, OCPS-compliant syntax.</summary>
    public static CronParserOptions Default { get; } = new();

    /// <summary>How the seconds field is handled. Default <see cref="SecondsMode.Optional"/>.</summary>
    public SecondsMode Seconds { get; init; } = SecondsMode.Optional;

    /// <summary>How the year field is handled. Default <see cref="YearMode.Optional"/>.</summary>
    public YearMode Year { get; init; } = YearMode.Optional;

    /// <summary>
    /// When <c>true</c>, a day must match both the day-of-month and the day-of-week field (AND).
    /// Default <c>false</c>: a day matching either field matches (OR), as in Vixie cron.
    /// A <c>+</c> prefix on the day-of-week field enables this for a single pattern.
    /// </summary>
    public bool RequireBothDayFields { get; init; }

    /// <summary>
    /// Use Quartz-style weekday numbering, where <c>1</c> is Sunday and <c>7</c> is Saturday.
    /// </summary>
    public bool QuartzWeekdays { get; init; }

    /// <summary>
    /// Accept the non-standard step syntax <c>0/10</c> (start at 0, step 10) and <c>/10</c> (same as <c>*/10</c>).
    /// Not compliant with OCPS / Vixie cron. Default <c>false</c>.
    /// </summary>
    public bool LenientSteps { get; init; }

    /// <summary>
    /// Which patterns count as fixed-time jobs in DST handling. Default <see cref="Croner.FixedTimeRule.SingleValue"/>.
    /// </summary>
    public FixedTimeRule FixedTimeRule { get; init; } = FixedTimeRule.SingleValue;
}

/// <summary>
/// Parses cron pattern strings into <see cref="Cron"/> instances.
/// </summary>
internal sealed class CronParser
{
    private static readonly char[] BaseAllowed =
        { '*', '/', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ',', '-' };

    private static readonly string[] WeekdayNames = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

    private static readonly string[] MonthNames =
        { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    /// <summary>Creates a parser with the given options, or the defaults.</summary>
    public CronParser(CronParserOptions? options = null)
    {
        Options = options ?? CronParserOptions.Default;
    }

    /// <summary>The options this parser uses.</summary>
    public CronParserOptions Options { get; }

    /// <summary>Parses a cron pattern.</summary>
    /// <exception cref="CronException">The pattern is invalid.</exception>
    public Cron Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        string upper = pattern.ToUpperInvariant().Trim();
        if (upper.Length == 0)
        {
            throw new CronException(CronErrorKind.EmptyPattern);
        }

        if (upper.Contains('@'))
        {
            upper = HandleNicknames(upper, Options.Seconds == SecondsMode.Required, Options.Year == YearMode.Required);
        }

        var parts = new List<string>(upper.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        int numParts = parts.Count;

        if (numParts == 5)
        {
            parts.Insert(0, "0");
        }
        else if (Options.Seconds == SecondsMode.Disallowed)
        {
            throw new CronException(CronErrorKind.InvalidPattern, "Pattern must have 5 fields when seconds are disallowed.");
        }

        if (parts.Count == 6)
        {
            parts.Add("*");
        }
        else if (Options.Year == YearMode.Disallowed)
        {
            throw new CronException(CronErrorKind.InvalidPattern, "Pattern must have 5 or 6 fields when years are disallowed.");
        }

        if (Options.Seconds == SecondsMode.Required)
        {
            if (Options.Year == YearMode.Required && numParts != 7)
            {
                throw new CronException(CronErrorKind.InvalidPattern, "Pattern must have 7 fields when seconds and years are required.");
            }
            if (Options.Year == YearMode.Disallowed && numParts != 6)
            {
                throw new CronException(CronErrorKind.InvalidPattern, "Pattern must have 6 fields when seconds are required and years are disallowed.");
            }
            if (Options.Year == YearMode.Optional && numParts is not (6 or 7))
            {
                throw new CronException(CronErrorKind.InvalidPattern, "Pattern must have 6 or 7 fields when seconds are required and years are optional.");
            }
        }
        else if (Options.Year == YearMode.Required && numParts != 7)
        {
            throw new CronException(CronErrorKind.InvalidPattern, "Pattern must have 7 fields when years are required.");
        }
        else if (numParts is < 5 or > 7)
        {
            throw new CronException(CronErrorKind.InvalidPattern, "Pattern must have between 5 and 7 fields.");
        }

        // Names are only valid in their own field, so "0 0 * MON *" is rejected instead of read as January.
        parts[4] = ReplaceNames(parts[4], "month", MonthNames, (i, _) => i + 1);
        bool quartz = Options.QuartzWeekdays;
        string dow = parts[5];
        parts[5] = ReplaceNames(dow, "day-of-week", WeekdayNames, (i, start) =>
        {
            // `-SUN` ends a range, so in standard mode it is read as 7 to make ranges like SAT-SUN work.
            // SUN-SUN stays Sunday only. In Quartz mode SUN is 1 and X-SUN is a range that wraps around the week.
            bool endsRange = start > 0 && dow[start - 1] == '-'
                && !(start >= 4 && string.CompareOrdinal(dow, start - 4, "SUN", 0, 3) == 0);
            return quartz ? i + 1 : i == 0 && endsRange ? 7 : i;
        });

        // '?' is an alias for '*' in day-of-month and day-of-week
        parts[3] = parts[3].Replace('?', '*');
        parts[5] = parts[5].Replace('?', '*');

        // '+' prefix on day-of-week: require both DOM and DOW to match
        bool domAndDowFromPattern = false;
        if (parts[5].StartsWith('+'))
        {
            domAndDowFromPattern = true;
            parts[5] = parts[5].Substring(1);
        }

        ThrowAtIllegalCharacters(parts);

        bool starDom = parts[3] == "*";
        bool starDow = parts[5] == "*";

        CronComponent Make(ushort min, ushort max, byte features, ushort offset, string field, bool wrapRanges = false)
        {
            var c = new CronComponent(min, max, features, offset);
            c.SetLenientSteps(Options.LenientSteps);
            c.SetWrapRanges(wrapRanges);
            c.Parse(field);
            return c;
        }

        var seconds = Make(0, 59, CronBits.None, 0, parts[0]);
        var minutes = Make(0, 59, CronBits.None, 0, parts[1]);
        var hours = Make(0, 23, CronBits.None, 0, parts[2]);
        var days = Make(1, 31, CronBits.Last | CronBits.ClosestWeekday, 0, parts[3]);
        var months = Make(1, 12, CronBits.None, 0, parts[4]);
        var daysOfWeek = Make(0, 7, CronBits.Last | CronBits.NthAll, (ushort)(Options.QuartzWeekdays ? 1 : 0), parts[5],
            wrapRanges: Options.QuartzWeekdays); // Quartz wraps SAT-SUN (7-1) and FRI-MON (6-2) around the week
        var years = Make((ushort)Cron.MinYear, (ushort)Cron.MaxYear, CronBits.None, 0, parts[6]);

        // 7 is Sunday too, in the standard numbering
        if (!Options.QuartzWeekdays)
        {
            foreach (byte bit in new[] { CronBits.All, CronBits.Nth1st, CronBits.Nth2nd, CronBits.Nth3rd, CronBits.Nth4th, CronBits.Nth5th, CronBits.Last })
            {
                if (daysOfWeek.IsBitSet(7, bit))
                {
                    daysOfWeek.UnsetBit(7, bit);
                    daysOfWeek.SetBit(0, bit);
                }
            }
        }

        return new Cron(new CronPattern(
            upper, seconds, minutes, hours, days, months, daysOfWeek, years,
            starDom, starDow, Options.RequireBothDayFields || domAndDowFromPattern, Options.FixedTimeRule));
    }

    /// <summary>Tries to parse a cron pattern. Returns <c>false</c> when it is <c>null</c> or invalid.</summary>
    public bool TryParse([NotNullWhen(true)] string? pattern, [NotNullWhen(true)] out Cron? cron)
    {
        if (pattern is null)
        {
            cron = null;
            return false;
        }
        try
        {
            cron = Parse(pattern);
            return true;
        }
        catch (CronException)
        {
            cron = null;
            return false;
        }
    }

    private static void ThrowAtIllegalCharacters(List<string> parts)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            string part = parts[i];
            foreach (char ch in part)
            {
                bool allowed = Array.IndexOf(BaseAllowed, ch) >= 0
                    || (i == 3 && ch is 'L' or 'W' or '?')
                    || (i == 5 && ch is '#' or 'L' or '?');
                if (!allowed)
                {
                    throw new CronException(CronErrorKind.IllegalCharacter,
                        $"'{ch}' in field '{part}'");
                }
            }
        }
    }

    internal static string HandleNicknames(string pattern, bool withSeconds, bool withYear)
    {
        pattern = pattern.Trim();
        string basePattern = pattern.ToUpperInvariant() switch
        {
            "@YEARLY" or "@ANNUALLY" => "0 0 1 1 *",
            "@MONTHLY" => "0 0 1 * *",
            "@WEEKLY" => "0 0 * * SUN",
            "@DAILY" or "@MIDNIGHT" => "0 0 * * *",
            "@HOURLY" => "0 * * * *",
            // OCPS 1.1: @reboot is event-based, not time-based
            "@REBOOT" => throw new CronException(CronErrorKind.InvalidPattern,
                "@reboot is not supported: it is an event, not a time. Run the job at application startup instead."),
            _ => pattern,
        };

        return (withSeconds ? "0 " : "") + basePattern + (withYear ? " *" : "");
    }

    /// <summary>
    /// Replaces the three-letter names in an upper-cased field with their numbers.
    /// </summary>
    /// <remarks>
    /// As in croner for JavaScript and croner-rust, a name has to stand alone: it may not follow a letter,
    /// digit, <c>/</c> or <c>#</c>, and may not be followed by a digit or a letter other than <c>L</c>
    /// (as in <c>FRIL</c>). So <c>SUN6</c>, <c>JANFEB</c>, <c>*/FRI</c> and <c>5#FRI</c> are rejected
    /// instead of being read as numbers. <paramref name="value"/> gets the index of the name and its position.
    /// </remarks>
    private static string ReplaceNames(string field, string fieldName, string[] names, Func<int, int, int> value)
    {
        bool hasLetter = false;
        foreach (char ch in field)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                hasLetter = true;
                break;
            }
        }
        if (!hasLetter)
        {
            return field;
        }

        var result = new StringBuilder(field.Length);
        int pos = 0;
        while (pos < field.Length)
        {
            int found = pos + 3 <= field.Length
                ? Array.FindIndex(names, n => string.CompareOrdinal(field, pos, n, 0, 3) == 0)
                : -1;
            if (found < 0)
            {
                // Not a name; anything illegal is rejected later.
                result.Append(field[pos]);
                pos++;
                continue;
            }

            bool beforeOk = pos == 0 || !(char.IsAsciiLetterOrDigit(field[pos - 1]) || field[pos - 1] is '/' or '#');
            bool afterOk = pos + 3 >= field.Length
                || !(char.IsAsciiDigit(field[pos + 3]) || (char.IsAsciiLetter(field[pos + 3]) && field[pos + 3] != 'L'));
            if (!beforeOk || !afterOk)
            {
                throw new CronException(CronErrorKind.IllegalCharacter,
                    $"the {fieldName} name '{names[found]}' in '{field}' has to stand alone: it can not be joined to a letter, digit, '/' or '#'.");
            }
            result.Append(value(found, pos).ToString(CultureInfo.InvariantCulture));
            pos += 3;
        }
        return result.ToString();
    }
}
