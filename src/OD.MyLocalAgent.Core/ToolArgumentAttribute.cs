using System.ComponentModel;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines runtime argument metadata for a tool method parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ToolArgumentAttribute : DescriptionAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolArgumentAttribute"/> class.
    /// </summary>
    /// <param name="kind">The deterministic JSON value kind accepted by the runtime.</param>
    /// <param name="description">One English sentence describing the argument.</param>
    public ToolArgumentAttribute(WorkflowArgumentKind kind, string description)
        : base(description)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the JSON argument value kind.
    /// </summary>
    public WorkflowArgumentKind Kind { get; }

    /// <summary>
    /// Gets or sets a realistic example value shown by authoring aids.
    /// </summary>
    public string? Example { get; set; }

    /// <summary>
    /// Gets or sets the exact enumeration names accepted by the runtime, in declaration order.
    /// </summary>
    public string[] AllowedValues { get; set; } = [];

    /// <summary>
    /// Gets or sets an explicit override for workflow placeholder support.
    /// </summary>
    public bool? SupportsPlaceholders { get; set; }

    /// <summary>
    /// Gets or sets an explicit override for argument requiredness when reflection alone is ambiguous.
    /// </summary>
    public bool? IsRequired { get; set; }

    /// <summary>
    /// Gets or sets whether authoring surfaces should provide a long-form text editor.
    /// </summary>
    public bool IsLongText { get; set; }

    /// <summary>Gets or sets whether nested string placeholders must remain text inside an object argument.</summary>
    public bool PreserveNestedPlaceholderStrings { get; set; }

    /// <summary>
    /// Gets or sets whether this short or format-constrained string is omitted from the workflow
    /// text editor even when it is present in workflow JSON.
    /// </summary>
    public bool IsExcludedFromWorkflowTextEditor { get; set; }

    /// <summary>Gets or sets the optional inclusive authoring minimum.</summary>
    public double Minimum { get; set; } = double.NaN;

    /// <summary>Gets or sets the optional inclusive authoring maximum.</summary>
    public double Maximum { get; set; } = double.NaN;

    /// <summary>Gets or sets the optional maximum number of characters accepted by a string argument.</summary>
    public int MaximumLength { get; set; }

    /// <summary>Gets or sets the optional semantic editor override.</summary>
    public WorkflowArgumentEditorHint EditorHint { get; set; }
}
