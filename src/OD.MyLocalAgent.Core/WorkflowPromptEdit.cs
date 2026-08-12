namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one targeted prompt edit keyed by workflow position.
/// </summary>
/// <param name="Position">The zero-based workflow position of the <c>ai.prompt</c> step.</param>
/// <param name="PromptText">The new prompt text, normalized to LF by the service.</param>
/// <param name="IncludeContext">The explicit accumulated-context choice.</param>
public sealed record WorkflowPromptEdit(
    int Position,
    string PromptText,
    bool IncludeContext)
{
    /// <summary>Gets the targeted long-text argument name.</summary>
    public string ArgumentName { get; init; } = "prompt";

    /// <summary>Gets the optional model override for this AI prompt step.</summary>
    public string? Model { get; init; }
}
