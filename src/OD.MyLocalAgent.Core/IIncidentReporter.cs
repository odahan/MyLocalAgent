namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides the single entry point for application incident reporting.
/// </summary>
public interface IIncidentReporter
{
    /// <summary>
    /// Occurs after an incident has been durably appended.
    /// </summary>
    event EventHandler<IncidentReportedEventArgs>? IncidentReported;

    /// <summary>
    /// Records an error-severity exception and returns its correlation identifier.
    /// </summary>
    /// <param name="exception">The exception to report.</param>
    /// <param name="source">The source page, task, or process hook.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated correlation identifier.</returns>
    Task<string> ReportAsync(
        Exception exception,
        string source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records an exception and returns its correlation identifier.
    /// </summary>
    /// <param name="exception">The exception to report.</param>
    /// <param name="source">The source page, task, or process hook.</param>
    /// <param name="severity">The incident severity.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated correlation identifier.</returns>
    Task<string> ReportAsync(
        Exception exception,
        string source,
        IncidentSeverity severity,
        CancellationToken cancellationToken);
}
