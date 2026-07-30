using CommunityToolkit.Mvvm.ComponentModel;
using OD.MyLocalAgent.Core;
using System.IO;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents one task-owned authorized local directory.
/// </summary>
public sealed partial class FileSystemAccessItem : ObservableObject
{
    /// <summary>
    /// Initializes a new authorized-location item.
    /// </summary>
    /// <param name="path">The normalized absolute directory path.</param>
    /// <param name="access">The persisted access mode.</param>
    public FileSystemAccessItem(string path, FileSystemAccessMode access)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        CanWrite = access == FileSystemAccessMode.ReadWrite;
        Status = GetStatus(path);
    }

    /// <summary>
    /// Gets the normalized absolute directory path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets a concise availability or network warning.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Gets or sets a value indicating whether mutation is authorized.
    /// </summary>
    [ObservableProperty]
    private bool _canWrite;

    /// <summary>
    /// Converts this item into the persisted task contract.
    /// </summary>
    /// <returns>The file-system access rule.</returns>
    public FileSystemAccessRule ToRule() =>
        new()
        {
            Path = Path,
            Access = CanWrite
                ? FileSystemAccessMode.ReadWrite
                : FileSystemAccessMode.ReadOnly,
        };

    private static string GetStatus(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return Directory.Exists(path)
                ? "Network path · S4U may not have Windows credentials"
                : "Unavailable network path · S4U may not have Windows credentials";
        }

        return Directory.Exists(path) ? "Available" : "Unavailable";
    }
}
