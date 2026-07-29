namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents a complete workflow or prompt-path execution.
/// </summary>
/// <param name="Success">Whether the execution completed without failures.</param>
/// <param name="Steps">The ordered workflow step results.</param>
/// <param name="FinalText">The last step output or prompt response.</param>
/// <param name="StartedUtc">The UTC execution start.</param>
/// <param name="EndedUtc">The UTC execution end.</param>
/// <param name="ErrorMessage">The execution-level error, when applicable.</param>
public sealed record WorkflowResult(
    bool Success,
    IReadOnlyList<WorkflowStepResult> Steps,
    string FinalText,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Gets the total execution duration.
    /// </summary>
    public TimeSpan Duration => EndedUtc - StartedUtc;
}
