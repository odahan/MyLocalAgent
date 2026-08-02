using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents one Tool Map argument as a read-only catalog table row.
/// </summary>
/// <param name="Definition">The normative argument metadata.</param>
public sealed record WorkflowToolArgumentRow(ToolArgumentDefinition Definition)
{
    /// <summary>
    /// Gets the argument name.
    /// </summary>
    public string Name => Definition.Name;

    /// <summary>
    /// Gets the accepted JSON value kind.
    /// </summary>
    public string Kind => Definition.Kind.ToString();

    /// <summary>
    /// Gets the requirement label.
    /// </summary>
    public string Requirement => Definition.IsRequired ? "Required" : "Optional";

    /// <summary>
    /// Gets the argument description.
    /// </summary>
    public string Description => Definition.Description;

    /// <summary>
    /// Gets the example value, or an empty cell.
    /// </summary>
    public string Example => Definition.Example ?? string.Empty;

    /// <summary>
    /// Gets the comma-separated allowed values, or an empty cell.
    /// </summary>
    public string AllowedValues => string.Join(", ", Definition.AllowedValues);

    /// <summary>
    /// Gets the placeholder-support label.
    /// </summary>
    public string PlaceholderSupport => Definition.SupportsPlaceholders ? "Yes" : "No";
}
