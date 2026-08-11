using System.ComponentModel;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies a public method as a tool alias and defines its execution metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ToolAttribute : DescriptionAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolAttribute"/> class.
    /// </summary>
    /// <param name="alias">The stable workflow alias.</param>
    /// <param name="description">One English sentence describing the operation and its output.</param>
    /// <param name="order">The unique positive integer display order within its group.</param>
    public ToolAttribute(string alias, string description, int order)
        : base(description)
    {
        Alias = alias;
        Order = order;
    }

    /// <summary>
    /// Gets the workflow alias name.
    /// </summary>
    public string Alias { get; }

    /// <summary>
    /// Gets the group-local display order.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Gets or sets the explicit agent function name. When null, derived automatically from the alias.
    /// </summary>
    public string? AgentFunctionName { get; set; }

    /// <summary>
    /// Gets or sets the optional sensitive capability required for invocation.
    /// </summary>
    public TaskCapability RequiredCapability { get; set; }
}
