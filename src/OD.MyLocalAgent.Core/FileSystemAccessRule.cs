namespace OD.MyLocalAgent.Core;

/// <summary>
/// Grants one task access to an absolute directory and its descendants.
/// </summary>
public sealed record FileSystemAccessRule
{
    /// <summary>
    /// Gets the absolute authorized directory or volume-root path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the access granted beneath <see cref="Path"/>.
    /// </summary>
    public FileSystemAccessMode Access { get; init; } = FileSystemAccessMode.ReadWrite;
}
