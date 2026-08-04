namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines stable tool-group names and their shared ordered catalog metadata.
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
    /// Gets every tool group in stable display order, including authoring-only groups.
    /// </summary>
    public static IReadOnlyList<ToolGroupDefinition> Catalog => ToolCatalog.Default.Groups;

    /// <summary>
    /// Gets task-whitelist groups in stable display and serialization order.
    /// </summary>
    public static IReadOnlyList<ToolGroupDefinition> TaskWhitelist => ToolCatalog.Default.TaskWhitelistGroups;

    /// <summary>
    /// Gets all supported task-whitelist group names.
    /// </summary>
    public static IReadOnlySet<string> All => ToolCatalog.Default.TaskWhitelistNames;

    /// <summary>
    /// Resolves catalog metadata for a stable group name.
    /// </summary>
    /// <param name="name">The case-insensitive tool-group name.</param>
    /// <returns>The matching metadata, or <see langword="null"/>.</returns>
    public static ToolGroupDefinition? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ToolCatalog.Default.FindGroup(name);
    }

    /// <summary>
    /// Gets the stable display position of a group.
    /// </summary>
    /// <param name="name">The case-insensitive tool-group name.</param>
    /// <returns>The group display order, or <see cref="int.MaxValue"/> when unknown.</returns>
    public static int GetDisplayOrder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ToolCatalog.Default.GetGroupDisplayOrder(name);
    }
}
