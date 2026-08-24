namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one ordered runtime argument of a Tool Map entry.
/// </summary>
/// <param name="Name">The camel-case argument name used in workflow JSON.</param>
/// <param name="Kind">The deterministic JSON value kind accepted by the runtime.</param>
/// <param name="IsRequired">Whether the argument must be supplied by the workflow author.</param>
/// <param name="Description">One English sentence describing the argument.</param>
public sealed record ToolArgumentDefinition(
    string Name,
    WorkflowArgumentKind Kind,
    bool IsRequired,
    string Description)
{
    /// <summary>
    /// Gets a realistic, non-secret example value shown by authoring aids.
    /// </summary>
    public string? Example { get; init; }

    /// <summary>
    /// Gets the exact enumeration names accepted by the runtime, in declaration order.
    /// </summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the runtime accepts the argument as a JSON string
    /// before workflow placeholder substitution.
    /// </summary>
    public bool SupportsPlaceholders { get; init; } = true;

    /// <summary>Gets whether authoring surfaces should prioritize this argument as long-form text.</summary>
    public bool IsLongText { get; init; }

    /// <summary>Gets whether this short or constrained string is excluded from the workflow text editor.</summary>
    public bool IsExcludedFromWorkflowTextEditor { get; init; }

    /// <summary>Gets the non-cancellation CLR value type reflected for this argument.</summary>
    public Type ValueType { get; init; } = typeof(string);

    /// <summary>Gets whether an explicitly included JSON null is compatible with the CLR parameter.</summary>
    public bool AllowsNull { get; init; }

    /// <summary>Gets whether the CLR parameter declares a serializable default value.</summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>Gets an immutable JSON-compatible reflected default value when one exists.</summary>
    public System.Text.Json.JsonElement? DefaultValue { get; init; }

    /// <summary>Gets the optional inclusive authoring minimum.</summary>
    public double? Minimum { get; init; }

    /// <summary>Gets the optional inclusive authoring maximum.</summary>
    public double? Maximum { get; init; }

    /// <summary>Gets the optional maximum number of characters accepted by a string argument.</summary>
    public int? MaximumLength { get; init; }

    /// <summary>Gets the optional semantic editor override.</summary>
    public WorkflowArgumentEditorHint EditorHint { get; init; }
}
