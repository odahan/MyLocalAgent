namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Describes the accumulated outcome of one Tool catalog session.
/// </summary>
public sealed record WorkflowToolCatalogResult
{
    /// <summary>
    /// Gets the final workflow editor text.
    /// </summary>
    public required string WorkflowText { get; init; }

    /// <summary>
    /// Gets the final UTF-16 workflow editor caret index.
    /// </summary>
    public required int CaretIndex { get; init; }

    /// <summary>
    /// Gets the non-AI tool groups that were explicitly accepted and are required by at least
    /// one inserted step.
    /// </summary>
    public IReadOnlyList<string> AcceptedToolGroups { get; init; } = [];

    /// <summary>
    /// Gets the ordered insertion summaries of the session.
    /// </summary>
    public IReadOnlyList<WorkflowToolCatalogInsertion> Insertions { get; init; } = [];

    /// <summary>
    /// Gets the content-free session warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether canonical formatting replaced manual formatting.
    /// </summary>
    public bool CanonicalFormattingApplied { get; init; }
}
