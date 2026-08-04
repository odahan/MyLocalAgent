using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines the single normative mapping shared by agents and workflows.
/// </summary>
public static class ToolMap
{
    /// <summary>
    /// Gets the prompt-step alias.
    /// </summary>
    public const string PromptAlias = "ai.prompt";

    /// <summary>
    /// Gets all normative Tool Map entries.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> Definitions => ToolCatalog.Default.Definitions;

    /// <summary>
    /// Resolves an alias case-insensitively.
    /// </summary>
    /// <param name="alias">The workflow alias.</param>
    /// <returns>The matching definition, or <see langword="null"/>.</returns>
    public static ToolDefinition? Find(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        return ToolCatalog.Default.Find(alias);
    }
}
