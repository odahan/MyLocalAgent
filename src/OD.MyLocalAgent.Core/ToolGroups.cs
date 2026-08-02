namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines the six task tool-group names.
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
    /// Gets the web-search tool-group name.
    /// </summary>
    public const string Web = "web";

    /// <summary>
    /// Gets the synthetic built-in AI group name.
    /// </summary>
    /// <remarks>
    /// This group exists only to group AI entries in authoring aids. It is never a task
    /// whitelist group and is deliberately excluded from <see cref="All"/>.
    /// </remarks>
    public const string Ai = "ai";

    /// <summary>
    /// Gets all supported tool-group names.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Gmail, Calendar, Dropbox, FileSystem, Http, Web],
        StringComparer.OrdinalIgnoreCase);
}
