namespace OD.MyLocalAgent.Core;

/// <summary>
/// Creates a tool instance bound to one immutable execution context.
/// </summary>
/// <typeparam name="TTool">The contextual tool type.</typeparam>
public interface IContextualToolFactory<out TTool>
{
    /// <summary>
    /// Creates a tool for the supplied execution context.
    /// </summary>
    /// <param name="context">The immutable tool execution context.</param>
    /// <returns>The contextual tool instance.</returns>
    TTool Create(ToolExecutionContext context);
}
