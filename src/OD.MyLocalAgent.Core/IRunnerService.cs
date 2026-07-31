namespace OD.MyLocalAgent.Core;

/// <summary>
/// Runs a task through the headless runner process and streams its output.
/// </summary>
public interface IRunnerService
{
    /// <summary>
    /// Gets the resolved runner executable path.
    /// </summary>
    string RunnerPath { get; }

    /// <summary>
    /// Runs one task in a cancellable child process.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="outputLine">An optional callback for stdout and stderr lines.</param>
    /// <param name="contextUpdated">An optional callback for workflow-context snapshots.</param>
    /// <param name="cancellationToken">A token that cancels and terminates the process.</param>
    /// <returns>The runner exit code.</returns>
    Task<int> RunTaskAsync(
        string taskId,
        Action<string>? outputLine,
        Action<string>? contextUpdated,
        CancellationToken cancellationToken);
}
