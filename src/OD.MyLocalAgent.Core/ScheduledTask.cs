using System.Text.Json.Serialization;

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
    /// Gets the whitelisted tool groups.
    /// </summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

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
