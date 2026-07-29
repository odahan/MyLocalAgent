namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies the error policy applied by a workflow or one of its steps.
/// </summary>
public enum WorkflowErrorPolicy
{
    /// <summary>
    /// Stops execution after the first failed step.
    /// </summary>
    StopOnError,

    /// <summary>
    /// Records a failed step and continues with the next step.
    /// </summary>
    ContinueOnError,

    /// <summary>
    /// Retries a failed step according to its retry settings.
    /// </summary>
    Retry,
}
