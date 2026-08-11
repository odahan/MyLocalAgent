namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents a sensitive operation rejected by task-owned authorization.
/// </summary>
public sealed class TaskAuthorizationException : InvalidOperationException
{
    /// <summary>Initializes a new instance.</summary>
    public TaskAuthorizationException(string? taskId, TaskCapability capability)
        : base(taskId is null
            ? $"This operation is not authorized outside a task. Required capability: '{capability}'."
            : $"Task '{taskId}' is not authorized for capability '{capability}'.")
    {
        TaskId = taskId;
        Capability = capability;
    }

    /// <summary>Gets the task identifier, or null for taskless use.</summary>
    public string? TaskId { get; }

    /// <summary>Gets the missing capability.</summary>
    public TaskCapability Capability { get; }
}
