using System;

namespace Croner;

/// <summary>
/// The kind of failure a <see cref="CronException"/> represents.
/// </summary>
public enum CronErrorKind
{
    /// <summary>The pattern string provided was empty.</summary>
    EmptyPattern,

    /// <summary>Encountered an invalid date while parsing or evaluating a cron pattern.</summary>
    InvalidDate,

    /// <summary>Encountered an invalid time while parsing or evaluating a cron pattern.</summary>
    InvalidTime,

    /// <summary>
    /// The search for the next or previous matching time exceeded a reasonable limit,
    /// typically because the pattern never matches (for example <c>0 0 30 2 *</c>).
    /// </summary>
    SearchLimitExceeded,

    /// <summary>The cron pattern provided is invalid, for example it has the wrong number of fields.</summary>
    InvalidPattern,

    /// <summary>The pattern contains characters that are not allowed.</summary>
    IllegalCharacter,

    /// <summary>A single field of the pattern is invalid, for example a value is out of range.</summary>
    InvalidField,
}

/// <summary>
/// Thrown when a cron pattern cannot be parsed or evaluated.
/// </summary>
public sealed class CronException : Exception
{
    /// <summary>Creates a new <see cref="CronException"/>.</summary>
    public CronException(CronErrorKind kind, string? detail = null)
        : base(FormatMessage(kind, detail))
    {
        Kind = kind;
        Detail = detail;
    }

    /// <summary>The kind of failure.</summary>
    public CronErrorKind Kind { get; }

    /// <summary>Additional detail about the failure, if any.</summary>
    public string? Detail { get; }

    private static string FormatMessage(CronErrorKind kind, string? detail) => kind switch
    {
        CronErrorKind.SearchLimitExceeded => "No matching time was found within the search limits.",
        CronErrorKind.EmptyPattern => "The pattern is empty.",
        CronErrorKind.InvalidDate => "Encountered an invalid date.",
        CronErrorKind.InvalidTime => "Encountered an invalid time.",
        CronErrorKind.InvalidPattern => $"Invalid pattern: {detail}",
        CronErrorKind.IllegalCharacter => $"Pattern contains an illegal character: {detail}",
        CronErrorKind.InvalidField => $"Invalid field: {detail}",
        _ => detail ?? kind.ToString(),
    };
}
