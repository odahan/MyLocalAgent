namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Displays one upcoming scheduled execution.
/// </summary>
/// <param name="TaskId">The task identifier.</param>
/// <param name="TaskName">The task name.</param>
/// <param name="ScheduledFor">The local scheduled time.</param>
public sealed record NextRunItem(
    string TaskId,
    string TaskName,
    string ScheduledFor);
