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

    private static readonly IReadOnlyList<ToolGroupDefinition> CatalogEntries =
        Array.AsReadOnly<ToolGroupDefinition>(
        [
            TaskGroup(Gmail, "Gmail", "Read or send Gmail messages through the configured account."),
            TaskGroup(Calendar, "Calendar", "Read or create Google Calendar events through the configured account."),
            TaskGroup(Dropbox, "Dropbox", "List, upload, or download Dropbox content."),
            TaskGroup(FileSystem, "File system", "Read or change files beneath authorized local paths."),
            TaskGroup(Http, "HTTP", "Send HTTP requests to hosts allowed by Settings."),
            TaskGroup(Web, "Web search (web) (cloud)", "Search the web through the configured cloud search provider."),
            new ToolGroupDefinition(
                Ai,
                "Built-in AI",
                "Run built-in AI workflow steps that do not require a task tool-group permission.",
                IsTaskWhitelistGroup: false),
        ]);

    private static readonly IReadOnlyDictionary<string, ToolGroupDefinition> CatalogByName =
        CatalogEntries.ToDictionary(
            static definition => definition.Name,
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<ToolGroupDefinition> TaskGroupEntries =
        Array.AsReadOnly(
            CatalogEntries
                .Where(static definition => definition.IsTaskWhitelistGroup)
                .ToArray());

    /// <summary>
    /// Gets every tool group in stable display order, including authoring-only groups.
    /// </summary>
    public static IReadOnlyList<ToolGroupDefinition> Catalog => CatalogEntries;

    /// <summary>
    /// Gets task-whitelist groups in stable display and serialization order.
    /// </summary>
    public static IReadOnlyList<ToolGroupDefinition> TaskWhitelist => TaskGroupEntries;

    /// <summary>
    /// Gets all supported task-whitelist group names.
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        TaskGroupEntries.Select(static definition => definition.Name),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves catalog metadata for a stable group name.
    /// </summary>
    /// <param name="name">The case-insensitive tool-group name.</param>
    /// <returns>The matching metadata, or <see langword="null"/>.</returns>
    public static ToolGroupDefinition? Find(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return CatalogByName.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets the stable display position of a group.
    /// </summary>
    /// <param name="name">The case-insensitive tool-group name.</param>
    /// <returns>The zero-based catalog position, or <see cref="int.MaxValue"/> when unknown.</returns>
    public static int GetDisplayOrder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        for (var index = 0; index < CatalogEntries.Count; index++)
        {
            if (string.Equals(
                    CatalogEntries[index].Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static ToolGroupDefinition TaskGroup(
        string name,
        string displayName,
        string description) =>
        new(name, displayName, description, IsTaskWhitelistGroup: true);
}
