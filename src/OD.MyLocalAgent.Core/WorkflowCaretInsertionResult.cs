namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes the deterministic outcome of one caret-aware workflow insertion.
/// </summary>
public sealed record WorkflowCaretInsertionResult
{
    /// <summary>
    /// Gets the final workflow text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Gets the final UTF-16 caret index, collapsed at the start of the inserted text.
    /// </summary>
    public required int CaretIndex { get; init; }

    /// <summary>
    /// Gets the zero-based top-level index at which the step was inserted.
    /// </summary>
    public required int InsertedIndex { get; init; }

    /// <summary>
    /// Gets a value indicating whether the structured insertion path succeeded.
    /// </summary>
    public required bool IsStructured { get; init; }

    /// <summary>
    /// Gets a value indicating whether the text was canonically serialized.
    /// </summary>
    public required bool CanonicalFormattingApplied { get; init; }

    /// <summary>
    /// Gets the content-free fallback warning, or <see langword="null"/> when structured
    /// insertion succeeded.
    /// </summary>
    public string? Warning { get; init; }
}
