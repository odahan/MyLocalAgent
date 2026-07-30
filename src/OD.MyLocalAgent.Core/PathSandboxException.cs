namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents a path that escapes the configured local sandbox.
/// </summary>
public sealed class PathSandboxException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PathSandboxException"/> class.
    /// </summary>
    /// <param name="path">The rejected path.</param>
    public PathSandboxException(string path)
        : base($"Path '{path}' is outside the configured file-system sandbox.")
    {
        Path = path;
    }

    /// <summary>
    /// Initializes a new instance with a safe rejection reason.
    /// </summary>
    /// <param name="path">The rejected path.</param>
    /// <param name="reason">The reason the path was rejected.</param>
    public PathSandboxException(string path, string reason)
        : base($"Path '{path}' was rejected: {reason}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Path = path;
    }

    /// <summary>
    /// Gets the rejected path.
    /// </summary>
    public string Path { get; }
}
