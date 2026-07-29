namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents one durable application incident.
/// </summary>
/// <param name="TimestampUtc">The UTC occurrence time.</param>
/// <param name="Severity">The incident severity.</param>
/// <param name="Source">The source page, task, or process hook.</param>
/// <param name="Message">The exception message.</param>
/// <param name="StackTrace">The complete exception representation.</param>
/// <param name="CorrelationId">The user-visible correlation identifier.</param>
public sealed record IncidentRecord(
    DateTimeOffset TimestampUtc,
    IncidentSeverity Severity,
    string Source,
    string Message,
    string StackTrace,
    string CorrelationId);
