namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one ordered tool group exposed by the shared tool catalog.
/// </summary>
/// <param name="Name">The stable group name stored in task JSON.</param>
/// <param name="DisplayName">The English label shown by user interfaces.</param>
/// <param name="Description">The English explanation shown by authoring surfaces.</param>
/// <param name="IsTaskWhitelistGroup">Whether tasks may persist the group in their whitelist.</param>
public sealed record ToolGroupDefinition(
    string Name,
    string DisplayName,
    string Description,
    bool IsTaskWhitelistGroup);
