namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Displays one recent execution-index entry.
/// </summary>
/// <param name="TaskId">The task identifier.</param>
/// <param name="Started">The local start time.</param>
/// <param name="Duration">The formatted duration.</param>
/// <param name="Status">The execution status.</param>
/// <param name="ReportPath">The absolute report path.</param>
public sealed record ExecutionHistoryItem(
    string TaskId,
    string Started,
    string Duration,
    string Status,
    string ReportPath);
