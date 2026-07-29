namespace OD.MyLocalAgent.Core;

/// <summary>
/// Configures the local file-system sandbox.
/// </summary>
public sealed class FileSystemOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "FileSystem";

    /// <summary>
    /// Gets or sets the root directory available to tools.
    /// </summary>
    public string RootPath { get; set; } = @"C:\OD.MyLocalAgent";

    /// <summary>
    /// Gets or sets the maximum number of bytes that a text-file read may return.
    /// </summary>
    public long MaxReadBytes { get; set; } = 1_048_576;
}
