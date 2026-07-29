namespace OD.MyLocalAgent.Core;

/// <summary>
/// Resolves relative paths beneath the configured file-system root.
/// </summary>
public interface IPathSandbox
{
    /// <summary>
    /// Resolves and validates a relative path.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <returns>The absolute sandboxed path.</returns>
    string Resolve(string path);

    /// <summary>
    /// Converts an absolute sandboxed path to a relative display path.
    /// </summary>
    /// <param name="absolutePath">The absolute sandboxed path.</param>
    /// <returns>The relative display path.</returns>
    string ToRelative(string absolutePath);
}
