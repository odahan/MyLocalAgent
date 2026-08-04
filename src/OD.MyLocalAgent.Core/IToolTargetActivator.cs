namespace OD.MyLocalAgent.Core;

/// <summary>
/// Activates tool target instances according to catalog lifetime metadata.
/// </summary>
public interface IToolTargetActivator
{
    /// <summary>
    /// Creates or resolves a tool target instance for a catalog type.
    /// </summary>
    /// <param name="toolType">The tool class declaring type.</param>
    /// <param name="context">The immutable execution context.</param>
    /// <returns>The target instance.</returns>
    object CreateTarget(Type toolType, ToolExecutionContext context);
}
