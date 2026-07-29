namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines the five task tool-group names.
/// </summary>
public static class ToolGroups
{
    /// <summary>
    /// Gets the Gmail tool-group name.
    /// </summary>
    public const string Gmail = "gmail";

    /// <summary>
    /// Gets the Calendar tool-group name.
    /// </summary>
    public const string Calendar = "calendar";

    /// <summary>
    /// Gets the Dropbox tool-group name.
    /// </summary>
    public const string Dropbox = "dropbox";

    /// <summary>
    /// Gets the file-system tool-group name.
    /// </summary>
    public const string FileSystem = "filesystem";

    /// <summary>
    /// Gets the HTTP tool-group name.
    /// </summary>
    public const string Http = "http";

    /// <summary>
    /// Gets all supported tool-group names.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Gmail, Calendar, Dropbox, FileSystem, Http],
        StringComparer.OrdinalIgnoreCase);
}
