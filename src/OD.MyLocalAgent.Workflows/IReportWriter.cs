using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Persists Markdown execution reports and the JSON Lines execution index.
/// </summary>
public interface IReportWriter
{
    /// <summary>
    /// Writes an execution report and appends its index entry.
    /// </summary>
    /// <param name="task">The executed task.</param>
    /// <param name="result">The complete execution result.</param>
    /// <param name="providerName">The configured or observed AI provider.</param>
    /// <param name="cancellationToken">A token that cancels report writing.</param>
    /// <returns>The persisted report path and Markdown.</returns>
    Task<ReportWriteResult> WriteAsync(
        ScheduledTask task,
        WorkflowResult result,
        string providerName,
        CancellationToken cancellationToken);
}
