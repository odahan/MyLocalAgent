using System.Text.Json.Serialization;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents a persisted scheduled local-agent task.
/// </summary>
public sealed record ScheduledTask
{
    /// <summary>
    /// Gets the stable lower-case task identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the user-facing task name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the five-field cron schedule.
    /// </summary>
    public required string Schedule { get; init; }

    /// <summary>
    /// Gets the prompt used by the task and exposed to workflows.
    /// </summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional task-level Temperature and Top P overrides.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GenerationOptions? GenerationOptions { get; init; }

    /// <summary>
    /// Gets the whitelisted tool groups.
    /// </summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>
    /// Gets the absolute directories additionally authorized for this task.
    /// </summary>
    public IReadOnlyList<FileSystemAccessRule> FileSystemAccess { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether SQLite read results require Guardrails protection.
    /// </summary>
    public bool SqliteGuardrailsEnabled { get; init; } = true;

    /// <summary>
    /// Gets the optional deterministic workflow.
    /// </summary>
    public IReadOnlyList<WorkflowStepDefinition> Workflow { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the task should be scheduled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the default workflow error policy.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<WorkflowErrorPolicy>))]
    public WorkflowErrorPolicy ErrorPolicy { get; init; } = WorkflowErrorPolicy.StopOnError;
}
