namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides data for a persisted task-store change.
/// </summary>
public sealed class TaskStoreChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TaskStoreChangedEventArgs"/> class.
    /// </summary>
    /// <param name="taskId">The affected task identifier.</param>
    /// <param name="changeKind">The change kind.</param>
    public TaskStoreChangedEventArgs(string taskId, TaskStoreChangeKind changeKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        TaskId = taskId;
        ChangeKind = changeKind;
    }

    /// <summary>
    /// Gets the affected task identifier.
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// Gets the change kind.
    /// </summary>
    public TaskStoreChangeKind ChangeKind { get; }
}
