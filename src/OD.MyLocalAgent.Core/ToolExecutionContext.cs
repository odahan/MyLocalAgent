namespace OD.MyLocalAgent.Core;

/// <summary>
/// Carries immutable task-specific authorization into one tool execution flow.
/// </summary>
public sealed record ToolExecutionContext
{
    /// <summary>
    /// Gets the context used by interactive tools that are not associated with a task.
    /// </summary>
    public static ToolExecutionContext Taskless { get; } = new(null, [], null);

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolExecutionContext"/> class.
    /// </summary>
    /// <param name="taskId">The task identifier, or <see langword="null"/>.</param>
    /// <param name="fileSystemAccess">The task's absolute-directory grants.</param>
    /// <param name="sqliteGuardrailsEnabled">The persisted SQLite Guardrails preference, or <see langword="null"/> for taskless use.</param>
    public ToolExecutionContext(
        string? taskId,
        IEnumerable<FileSystemAccessRule> fileSystemAccess,
        bool? sqliteGuardrailsEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystemAccess);
        TaskId = taskId;
        FileSystemAccess = fileSystemAccess.ToArray();
        SqliteGuardrailsEnabled = sqliteGuardrailsEnabled;
    }

    /// <summary>
    /// Gets the task identifier, or <see langword="null"/> for taskless interactive use.
    /// </summary>
    public string? TaskId { get; }

    /// <summary>
    /// Gets the immutable absolute-directory grants for this execution.
    /// </summary>
    public IReadOnlyList<FileSystemAccessRule> FileSystemAccess { get; }

    /// <summary>
    /// Gets the persisted SQLite Guardrails preference, or <see langword="null"/> for taskless use.
    /// </summary>
    public bool? SqliteGuardrailsEnabled { get; }

    /// <summary>
    /// Creates an execution context from a validated scheduled task.
    /// </summary>
    /// <param name="task">The task that owns the execution.</param>
    /// <returns>The immutable execution context.</returns>
    public static ToolExecutionContext FromTask(ScheduledTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new ToolExecutionContext(task.Id, task.FileSystemAccess, task.SqliteGuardrailsEnabled);
    }
}
