namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one supported workflow placeholder template.
/// </summary>
/// <param name="InsertText">The exact placeholder template inserted or copied by authoring aids.</param>
/// <param name="Description">One English sentence describing the substituted value.</param>
public sealed record WorkflowPlaceholderDefinition(
    string InsertText,
    string Description)
{
    /// <summary>
    /// Gets the text used by the <see cref="WorkflowPlaceholderCatalog.Definitions"/> projection.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="InsertText"/>. It differs only for templates whose documentation
    /// line also shows an alternative form that is not itself directly insertable.
    /// </remarks>
    public string DisplayText { get; init; } = InsertText;

    /// <summary>
    /// Gets the optional authoring details shown by the user interface only.
    /// </summary>
    /// <remarks>
    /// This text documents accepted arguments such as date/time formats. It is never part of
    /// the <see cref="WorkflowPlaceholderCatalog.Definitions"/> model-context projection.
    /// </remarks>
    public string? Details { get; init; }

    /// <summary>
    /// Gets a value indicating whether the template must be completed with a prior step id
    /// before it can be inserted.
    /// </summary>
    public bool RequiresPriorStepId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the template accepts a trailing structured selector
    /// such as a property path or an array index.
    /// </summary>
    public bool SupportsStructuredSelector { get; init; }
}
