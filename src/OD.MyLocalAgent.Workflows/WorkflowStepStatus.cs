namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Identifies the outcome of a workflow step.
/// </summary>
public enum WorkflowStepStatus
{
    /// <summary>
    /// The step completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The step failed after its effective error policy was applied.
    /// </summary>
    Failure,
}
