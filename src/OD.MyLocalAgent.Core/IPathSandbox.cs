namespace OD.MyLocalAgent.Core;

/// <summary>
/// Resolves paths against the global root and task-specific authorization.
/// </summary>
public interface IPathSandbox
{
    /// <summary>
    /// Resolves and validates a path for the requested access.
    /// </summary>
    /// <param name="path">A relative global-root path or absolute task-authorized path.</param>
    /// <param name="requiredAccess">The access required by the operation.</param>
    /// <param name="context">The immutable execution context.</param>
    /// <returns>The absolute sandboxed path.</returns>
    string Resolve(
        string path,
        FileSystemAccessMode requiredAccess,
        ToolExecutionContext context);

    /// <summary>
    /// Converts an absolute sandboxed path to a relative display path.
    /// </summary>
    /// <param name="absolutePath">The absolute sandboxed path.</param>
    /// <returns>The relative display path.</returns>
    string ToRelative(string absolutePath);
}
