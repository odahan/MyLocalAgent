namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies the severity of a recorded application incident.
/// </summary>
public enum IncidentSeverity
{
    /// <summary>
    /// A recoverable warning.
    /// </summary>
    Warning,

    /// <summary>
    /// An operation-level error.
    /// </summary>
    Error,

    /// <summary>
    /// A critical unhandled failure.
    /// </summary>
    Critical,
}
