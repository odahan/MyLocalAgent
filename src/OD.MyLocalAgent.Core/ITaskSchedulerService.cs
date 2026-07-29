namespace OD.MyLocalAgent.Core;

/// <summary>
/// Mirrors the task store into rolling Windows Task Scheduler registrations.
/// </summary>
public interface ITaskSchedulerService
{
    /// <summary>
    /// Registers the next occurrence of an enabled task.
    /// </summary>
    Task RegisterAsync(ScheduledTask task, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a task registration.
    /// </summary>
    Task UnregisterAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether a task is registered.
    /// </summary>
    bool IsRegistered(string id);

    /// <summary>
    /// Computes the next run from the task store and cron expression.
    /// </summary>
    Task<DateTimeOffset?> GetNextRunAsync(
        string id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Repairs all enabled, disabled, and stale registrations.
    /// </summary>
    Task<SchedulerResyncResult> ResyncAllAsync(CancellationToken cancellationToken);
}
