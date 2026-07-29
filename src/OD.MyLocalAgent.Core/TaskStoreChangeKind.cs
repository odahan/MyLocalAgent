namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies a persisted task-store change.
/// </summary>
public enum TaskStoreChangeKind
{
    /// <summary>
    /// A task was created or updated.
    /// </summary>
    Saved,

    /// <summary>
    /// A task was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// An external file-system change was observed.
    /// </summary>
    External,
}
