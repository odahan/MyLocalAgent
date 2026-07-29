namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents an unresolved workflow placeholder.
/// </summary>
public sealed class WorkflowPlaceholderException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowPlaceholderException"/> class.
    /// </summary>
    /// <param name="placeholder">The unresolved placeholder name.</param>
    /// <param name="stepId">The step being prepared.</param>
    public WorkflowPlaceholderException(string placeholder, string stepId)
        : base(
            $"Workflow step '{stepId}' references unknown placeholder '{{{{{placeholder}}}}}'.")
    {
        Placeholder = placeholder;
        StepId = stepId;
    }

    /// <summary>
    /// Gets the unresolved placeholder name.
    /// </summary>
    public string Placeholder { get; }

    /// <summary>
    /// Gets the step being prepared.
    /// </summary>
    public string StepId { get; }
}
