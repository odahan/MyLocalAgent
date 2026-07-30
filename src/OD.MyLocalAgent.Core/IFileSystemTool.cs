namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides authorized local file-system tool operations.
/// </summary>
public interface IFileSystemTool
{
    /// <summary>
    /// Lists a directory.
    /// </summary>
    Task<string> ListAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a text file.
    /// </summary>
    Task<string> ReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a text file.
    /// </summary>
    Task<string> WriteAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a file or directory.
    /// </summary>
    Task<string> MoveAsync(string from, string to, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a file or directory when the safety gate is enabled.
    /// </summary>
    Task<string> DeleteAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a directory.
    /// </summary>
    Task<string> CreateDirectoryAsync(string path, CancellationToken cancellationToken);
}
