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
    /// Writes a text file using the requested destination mode.
    /// </summary>
    Task<string> WriteAsync(
        string path,
        string content,
        string mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a file or directory.
    /// </summary>
    Task<string> MoveAsync(string from, string to, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a file or directory to the Windows Recycle Bin.
    /// </summary>
    Task<string> MoveToBinAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Renames a file while keeping it in the same directory.
    /// </summary>
    Task<string> RenameAsync(
        string path,
        string newName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Changes a file extension while keeping its directory and base name.
    /// </summary>
    Task<string> SetExtensionAsync(
        string path,
        string extension,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a file or directory when the safety gate is enabled.
    /// </summary>
    Task<string> DeleteAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a directory.
    /// </summary>
    Task<string> CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Computes a SHA-256 hash for an authorized file.
    /// </summary>
    Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Compares two authorized files using SHA-256 hashes.
    /// </summary>
    Task<string> CompareByHashAsync(
        string pathA,
        string pathB,
        CancellationToken cancellationToken);

    /// <summary>
    /// Converts an authorized Markdown file to a standalone HTML file.
    /// </summary>
    Task<string> MarkdownToHtmlAsync(
        string sourcePath,
        string? destinationPath = null,
        MarkdownHtmlTheme theme = MarkdownHtmlTheme.Dark,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts plain text or principal Markdown structure from an authorized HTML file.
    /// </summary>
    Task<string> HtmlToTextAsync(
        string sourcePath,
        HtmlTextFormat format = HtmlTextFormat.Text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file, URI, or application through the Windows shell.
    /// </summary>
    Task<string> ShellExecuteAsync(
        string target,
        string? arguments = null,
        bool waitForExit = false,
        bool captureOutput = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a PowerShell script.
    /// </summary>
    Task<string> PowerShellAsync(
        string script,
        bool waitForExit = true,
        bool captureOutput = true,
        CancellationToken cancellationToken = default);
}
