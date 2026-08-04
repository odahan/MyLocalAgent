namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines the lifetime and activation strategy of a tool implementation class.
/// </summary>
public enum ToolActivationKind
{
    /// <summary>
    /// The target instance is shared and task-independent.
    /// </summary>
    Singleton,

    /// <summary>
    /// The target instance is created bound to a specific execution context.
    /// </summary>
    Contextual,
}
