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
}
