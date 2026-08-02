namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines the deterministic JSON value kinds accepted by a workflow step argument.
/// </summary>
public enum WorkflowArgumentKind
{
    /// <summary>
    /// A textual value, including dates, times, and URIs bound from JSON strings.
    /// </summary>
    String,

    /// <summary>
    /// An integral numeric value.
    /// </summary>
    Integer,

    /// <summary>
    /// A fractional numeric value.
    /// </summary>
    Number,

    /// <summary>
    /// A boolean value.
    /// </summary>
    Boolean,

    /// <summary>
    /// A value restricted to a closed set of enumeration names.
    /// </summary>
    Enumeration,
}
