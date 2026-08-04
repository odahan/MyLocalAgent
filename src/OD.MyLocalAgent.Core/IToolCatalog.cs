namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes the immutable canonical catalog snapshot of tool groups and definitions.
/// </summary>
public interface IToolCatalog
{
    /// <summary>
    /// Gets all tool group definitions in catalog order.
    /// </summary>
    IReadOnlyList<ToolGroupDefinition> Groups { get; }

    /// <summary>
    /// Gets task-whitelist group definitions in catalog order.
    /// </summary>
    IReadOnlyList<ToolGroupDefinition> TaskWhitelistGroups { get; }

    /// <summary>
    /// Gets the set of task-whitelist group names.
    /// </summary>
    IReadOnlySet<string> TaskWhitelistNames { get; }

    /// <summary>
    /// Gets all tool definitions in catalog order.
    /// </summary>
    IReadOnlyList<ToolDefinition> Definitions { get; }

    /// <summary>
    /// Finds a group definition by case-insensitive name.
    /// </summary>

    ToolGroupDefinition? FindGroup(string name);

    /// <summary>
    /// Finds a tool definition by case-insensitive alias.
    /// </summary>
    ToolDefinition? Find(string alias);

    /// <summary>
    /// Gets the display order of a group by name.
    /// </summary>
    int GetGroupDisplayOrder(string name);
}
