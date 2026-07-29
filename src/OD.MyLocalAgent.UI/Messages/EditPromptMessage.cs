namespace OD.MyLocalAgent.UI.Messages;

/// <summary>
/// Requests the prompt editor for an existing task.
/// </summary>
/// <param name="TaskId">The task identifier.</param>
public sealed record EditPromptMessage(string TaskId);
