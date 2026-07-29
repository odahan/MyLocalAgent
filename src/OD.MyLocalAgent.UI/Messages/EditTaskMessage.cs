namespace OD.MyLocalAgent.UI.Messages;

/// <summary>
/// Requests the task editor for a new or existing task.
/// </summary>
/// <param name="TaskId">The existing task identifier, or <see langword="null"/> for a new task.</param>
public sealed record EditTaskMessage(string? TaskId);
