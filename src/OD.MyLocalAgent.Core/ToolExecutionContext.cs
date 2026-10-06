namespace OD.MyLocalAgent.Core;

/// <summary>
/// Carries immutable task-specific authorization into one tool execution flow.
/// </summary>
public sealed record ToolExecutionContext
{
    /// <summary>
    /// Gets the context used by interactive tools that are not associated with a task.
    /// </summary>
    public static ToolExecutionContext Taskless { get; } = new(null, [], [], null, null, []);

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolExecutionContext"/> class.
    /// </summary>
    /// <param name="taskId">The task identifier, or <see langword="null"/>.</param>
    /// <param name="fileSystemAccess">The task's absolute-directory grants.</param>
    /// <param name="capabilities">The task's sensitive capability grants.</param>
    /// <param name="sqliteGuardrailsEnabled">The persisted SQLite Guardrails preference, or <see langword="null"/> for taskless use.</param>
    /// <param name="useGlobalSystemPrompt">Whether configured task system prompts apply, or <see langword="null"/> for taskless use.</param>
    /// <param name="knowledgeTags">The normalized Knowledge tags authorized for this execution.</param>
    /// <param name="surface">The explicit invocation surface.</param>
    public ToolExecutionContext(
        string? taskId,
        IEnumerable<FileSystemAccessRule> fileSystemAccess,
        IEnumerable<TaskCapability> capabilities,
        bool? sqliteGuardrailsEnabled = null,
        bool? useGlobalSystemPrompt = null,
        IEnumerable<string>? knowledgeTags = null,
        ToolInvocationSurface surface = ToolInvocationSurface.Agent)
    {
        ArgumentNullException.ThrowIfNull(fileSystemAccess);
        ArgumentNullException.ThrowIfNull(capabilities);
        TaskId = taskId;
        Surface = surface;
        FileSystemAccess = fileSystemAccess.ToArray();
        Capabilities = capabilities.ToHashSet();
        SqliteGuardrailsEnabled = sqliteGuardrailsEnabled;
        UseGlobalSystemPrompt = useGlobalSystemPrompt;
        KnowledgeTags = KnowledgeTagPolicy.NormalizeScope(knowledgeTags);
    }

    /// <summary>
    /// Initializes a context without sensitive capabilities.
    /// </summary>
    public ToolExecutionContext(
        string? taskId,
        IEnumerable<FileSystemAccessRule> fileSystemAccess,
        bool? sqliteGuardrailsEnabled = null,
        bool? useGlobalSystemPrompt = null)
        : this(taskId, fileSystemAccess, [], sqliteGuardrailsEnabled, useGlobalSystemPrompt)
    {
    }

    /// <summary>
    /// Gets the task identifier, or <see langword="null"/> for taskless interactive use.
    /// </summary>
    public string? TaskId { get; }

    /// <summary>Gets the explicit invocation surface, preserved by authorization projections.</summary>
    public ToolInvocationSurface Surface { get; }

    /// <summary>Gets the immutable workflow run correlation, when present.</summary>
    public string? RunId { get; init; }
    /// <summary>Gets the immutable workflow step correlation, when present.</summary>
    public string? StepId { get; init; }

    /// <summary>
    /// Gets the immutable absolute-directory grants for this execution.
    /// </summary>
    public IReadOnlyList<FileSystemAccessRule> FileSystemAccess { get; }

    /// <summary>Gets the immutable sensitive capabilities granted to this execution.</summary>
    public IReadOnlySet<TaskCapability> Capabilities { get; }

    /// <summary>
    /// Gets the persisted SQLite Guardrails preference, or <see langword="null"/> for taskless use.
    /// </summary>
    public bool? SqliteGuardrailsEnabled { get; }

    /// <summary>
    /// Gets whether project-wide AI instructions apply, or <see langword="null"/> for taskless use.
    /// </summary>
    public bool? UseGlobalSystemPrompt { get; }

    /// <summary>Gets the immutable normalized Knowledge tag authorization.</summary>
    public IReadOnlyList<string> KnowledgeTags { get; }

    /// <summary>Creates a context whose Knowledge scope is narrowed without changing other grants.</summary>
    public ToolExecutionContext WithKnowledgeScope(IEnumerable<string>? requestedTags) =>
        new(
            TaskId,
            FileSystemAccess,
            Capabilities,
            SqliteGuardrailsEnabled,
            UseGlobalSystemPrompt,
            KnowledgeTagPolicy.Narrow(KnowledgeTags, requestedTags),
            Surface) { RunId = RunId, StepId = StepId };

    /// <summary>
    /// Creates an execution context from a validated scheduled task.
    /// </summary>
    /// <param name="task">The task that owns the execution.</param>
    /// <param name="surface">The explicit invocation surface.</param>
    /// <returns>The immutable execution context.</returns>
    public static ToolExecutionContext FromTask(ScheduledTask task, ToolInvocationSurface surface = ToolInvocationSurface.Agent)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new ToolExecutionContext(
            task.Id,
            task.FileSystemAccess,
            task.Capabilities,
            task.SqliteGuardrailsEnabled,
            task.UseGlobalSystemPrompt,
            task.KnowledgeAccess?.AllowedTags,
            surface);
    }
}
