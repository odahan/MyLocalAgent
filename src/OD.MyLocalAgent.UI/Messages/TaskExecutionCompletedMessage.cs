namespace OD.MyLocalAgent.UI.Messages;

/// <summary>
/// Announces completion of a child-runner execution.
/// </summary>
/// <param name="TaskId">The executed task identifier.</param>
/// <param name="ExitCode">The runner process exit code.</param>
public sealed record TaskExecutionCompletedMessage(string TaskId, int ExitCode);
