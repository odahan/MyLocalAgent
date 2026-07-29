namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Represents one task-management grid row.
/// </summary>
/// <param name="Id">The stable task identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Schedule">The five-field cron schedule.</param>
/// <param name="NextRun">The formatted next occurrence.</param>
/// <param name="LastRun">The formatted latest execution time.</param>
/// <param name="LastStatus">The latest execution status.</param>
/// <param name="IsEnabled">Whether the task is enabled.</param>
/// <param name="IsRegistered">Whether Windows Task Scheduler contains its mirror.</param>
public sealed record TaskListItem(
    string Id,
    string Name,
    string Schedule,
    string NextRun,
    string LastRun,
    string LastStatus,
    bool IsEnabled,
    bool IsRegistered)
{
    /// <summary>
    /// Gets the registration action label.
    /// </summary>
    public string RegistrationAction => IsRegistered ? "Unregister" : "Register";
}
