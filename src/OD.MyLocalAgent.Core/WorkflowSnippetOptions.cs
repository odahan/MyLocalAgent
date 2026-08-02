namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes the deterministic insertion options selected by a workflow authoring aid.
/// </summary>
public sealed record WorkflowSnippetOptions
{
    /// <summary>
    /// Gets a value indicating whether optional arguments are emitted.
    /// </summary>
    /// <remarks>
    /// This option never controls the <c>ai.prompt</c> <c>includeContext</c> argument, which is
    /// governed solely by <see cref="IncludeAccumulatedContext"/>.
    /// </remarks>
    public bool IncludeOptionalArguments { get; init; }

    /// <summary>
    /// Gets a value indicating whether the step-level error policy block is emitted.
    /// </summary>
    public bool IncludeErrorPolicy { get; init; }

    /// <summary>
    /// Gets a value indicating whether the AI generation options block is emitted.
    /// </summary>
    /// <remarks>Ignored for entries that are not AI steps.</remarks>
    public bool IncludeGenerationOptions { get; init; }

    /// <summary>
    /// Gets a value indicating whether a new <c>ai.prompt</c> step receives the complete
    /// accumulated workflow context.
    /// </summary>
    /// <remarks>
    /// The value is always emitted explicitly for <c>ai.prompt</c>, so the author's choice is
    /// never inferred from omission.
    /// </remarks>
    public bool IncludeAccumulatedContext { get; init; } = true;
}
