namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies the outcome of parsing and formatting a date/time placeholder body.
/// </summary>
public enum DateTimePlaceholderStatus
{
    /// <summary>
    /// The body is not a supported date/time placeholder.
    /// </summary>
    NotRecognized,

    /// <summary>
    /// The body was formatted successfully.
    /// </summary>
    Resolved,

    /// <summary>
    /// The body names a date/time placeholder but its format is invalid.
    /// </summary>
    InvalidFormat,
}
