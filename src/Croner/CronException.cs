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
    TimeSearchLimitExceeded,

    /// <summary>The cron pattern provided is invalid, for example it has the wrong number of fields.</summary>
    InvalidPattern,

    /// <summary>The pattern contains characters that are not allowed.</summary>
    IllegalCharacters,

    /// <summary>A single field of the pattern is invalid, for example a value is out of range.</summary>
    ComponentError,
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
        CronErrorKind.TimeSearchLimitExceeded => "CronScheduler time search limit exceeded.",
        CronErrorKind.EmptyPattern => "CronPattern cannot be an empty string.",
        CronErrorKind.InvalidDate => "CronScheduler encountered an invalid date.",
        CronErrorKind.InvalidTime => "CronScheduler encountered an invalid time.",
        CronErrorKind.InvalidPattern => $"Invalid pattern: {detail}",
        CronErrorKind.IllegalCharacters => $"Pattern contains illegal characters: {detail}",
        CronErrorKind.ComponentError => $"Component error: {detail}",
        _ => detail ?? kind.ToString(),
    };
}
