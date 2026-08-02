namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Describes the single applied outcome of one Workflow Prompt Editor session.
/// </summary>
public sealed record WorkflowPromptEditorResult
{
    /// <summary>
    /// Gets the canonical workflow text.
    /// </summary>
    public required string WorkflowText { get; init; }

    /// <summary>
    /// Gets the number of entries whose prompt text changed.
    /// </summary>
    public int ModifiedPromptCount { get; init; }

    /// <summary>
    /// Gets the number of entries whose <c>includeContext</c> value changed.
    /// </summary>
    public int ModifiedContextCount { get; init; }

    /// <summary>
    /// Gets the ordered modified positions and effective ids.
    /// </summary>
    public IReadOnlyList<WorkflowPromptEditorChange> ModifiedSteps { get; init; } = [];

    /// <summary>
    /// Gets the content-free context-duplication warnings, keyed by display position.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether canonical formatting replaced manual formatting.
    /// </summary>
    public bool CanonicalFormattingApplied { get; init; } = true;
}
