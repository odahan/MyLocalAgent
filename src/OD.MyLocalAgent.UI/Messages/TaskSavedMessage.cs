namespace OD.MyLocalAgent.UI.Messages;

/// <summary>
/// Announces that a task was persisted and synchronized.
/// </summary>
/// <param name="TaskId">The saved task identifier.</param>
public sealed record TaskSavedMessage(string TaskId);
