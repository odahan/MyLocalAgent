using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Executes deterministic task workflows.
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Executes the task's workflow sequentially.
    /// </summary>
    /// <param name="task">The task containing a non-empty workflow.</param>
    /// <param name="cancellationToken">A token that cancels the execution.</param>
    /// <returns>The complete workflow result.</returns>
    Task<WorkflowResult> ExecuteAsync(
        ScheduledTask task,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes the workflow and publishes each accumulated successful-step context snapshot.
    /// </summary>
    /// <param name="task">The task containing a non-empty workflow.</param>
    /// <param name="contextUpdated">An optional callback receiving context JSON after each successful step.</param>
    /// <param name="cancellationToken">A token that cancels the execution.</param>
    /// <returns>The complete workflow result.</returns>
    Task<WorkflowResult> ExecuteAsync(
        ScheduledTask task,
        Action<string>? contextUpdated,
        CancellationToken cancellationToken);
}
