namespace OD.MyLocalAgent.Core;

/// <summary>
/// Persists one validated JSON file per scheduled task.
/// </summary>
public interface ITaskStore
{
    /// <summary>
    /// Occurs when a task file changes.
    /// </summary>
    event EventHandler<TaskStoreChangedEventArgs>? TasksChanged;

    /// <summary>
    /// Gets a task by identifier.
    /// </summary>
    /// <param name="id">The task identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The task, or <see langword="null"/> when absent.</returns>
    Task<ScheduledTask?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets every persisted task.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The validated tasks ordered by identifier.</returns>
    Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves a validated task.
    /// </summary>
    /// <param name="task">The task to save.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The task as persisted, including store-owned timestamps.</returns>
    Task<ScheduledTask> SaveAsync(ScheduledTask task, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a persisted task definition into the archive directory.
    /// </summary>
    /// <param name="id">The identifier of the task to archive.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The archive file path, or <see langword="null"/> when the task was absent.</returns>
    Task<string?> ArchiveAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a task.
    /// </summary>
    /// <param name="id">The task identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns><see langword="true"/> when a task was deleted.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}
