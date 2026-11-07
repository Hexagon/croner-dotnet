using System;
using System.Collections.Generic;

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
    public bool DomAndDow { get; init; }

    /// <summary>
    /// Use Quartz-style weekday numbering, where <c>1</c> is Sunday and <c>7</c> is Saturday.
    /// </summary>
    public bool AlternativeWeekdays { get; init; }

    /// <summary>
    /// Accept the non-standard step syntax <c>0/10</c> (start at 0, step 10) and <c>/10</c> (same as <c>*/10</c>).
    /// Not compliant with OCPS / Vixie cron. Default <c>false</c>.
    /// </summary>
    public bool SloppyRanges { get; init; }
}

/// <summary>
/// Parses cron pattern strings into <see cref="Cron"/> instances.
/// </summary>
public sealed class CronParser
{
    private static readonly char[] BaseAllowed =
        { '*', '/', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ',', '-' };

    private static readonly (string Name, string Value)[] Weekdays =
    {
        ("-SUN", "-7"), ("SUN", "0"), ("MON", "1"), ("TUE", "2"),
        ("WED", "3"), ("THU", "4"), ("FRI", "5"), ("SAT", "6"),
    };

    private static readonly (string Name, string Value)[] AlternativeWeekdayNames =
    {
        ("-SUN", "-1"), ("SUN", "1"), ("MON", "2"), ("TUE", "3"),
        ("WED", "4"), ("THU", "5"), ("FRI", "6"), ("SAT", "7"),
    };

    private static readonly (string Name, string Value)[] MonthNames =
    {
        ("JAN", "1"), ("FEB", "2"), ("MAR", "3"), ("APR", "4"), ("MAY", "5"), ("JUN", "6"),
        ("JUL", "7"), ("AUG", "8"), ("SEP", "9"), ("OCT", "10"), ("NOV", "11"), ("DEC", "12"),
    };

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
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        string upper = pattern.ToUpperInvariant().Trim();
        if (upper.Length == 0)
        {
            throw new CronException(CronErrorKind.EmptyPattern);
        }

        if (upper.Contains('@'))
        {
            upper = HandleNicknames(upper, Options.Seconds == SecondsMode.Required, Options.Year == YearMode.Required);
        }

        string normalized = Replace(upper, Options.AlternativeWeekdays ? AlternativeWeekdayNames : Weekdays);
        normalized = Replace(normalized, MonthNames);

        var parts = new List<string>(normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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

        CronComponent Make(ushort min, ushort max, byte features, ushort offset, string field)
        {
            var c = new CronComponent(min, max, features, offset);
            c.SetSloppyRanges(Options.SloppyRanges);
            c.Parse(field);
            return c;
        }

        var seconds = Make(0, 59, CronBits.None, 0, parts[0]);
        var minutes = Make(0, 59, CronBits.None, 0, parts[1]);
        var hours = Make(0, 23, CronBits.None, 0, parts[2]);
        var days = Make(1, 31, CronBits.Last | CronBits.ClosestWeekday, 0, parts[3]);
        var months = Make(1, 12, CronBits.None, 0, parts[4]);
        var daysOfWeek = Make(0, 7, CronBits.Last | CronBits.NthAll, (ushort)(Options.AlternativeWeekdays ? 1 : 0), parts[5]);
        var years = Make((ushort)Cron.YearLowerLimit, (ushort)Cron.YearUpperLimit, CronBits.None, 0, parts[6]);

        // 7 is Sunday too, in the standard numbering
        if (!Options.AlternativeWeekdays)
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
            starDom, starDow, Options.DomAndDow || domAndDowFromPattern));
    }

    /// <summary>Tries to parse a cron pattern.</summary>
    public bool TryParse(string pattern, out Cron? cron)
    {
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
                    throw new CronException(CronErrorKind.IllegalCharacters,
                        $"CronPattern contains illegal character '{ch}' in part '{part}'");
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

    private static string Replace(string pattern, (string Name, string Value)[] names)
    {
        foreach (var (name, value) in names)
        {
            pattern = pattern.Replace(name, value, StringComparison.Ordinal);
        }
        return pattern;
    }
}
