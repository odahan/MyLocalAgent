using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents one Tool Map entry in the catalog list without duplicating its metadata.
/// </summary>
public sealed class WorkflowToolCatalogItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowToolCatalogItem"/> class.
    /// </summary>
    /// <param name="tool">The normative Tool Map entry.</param>
    /// <param name="isWhitelisted">Whether the task already grants the required tool group.</param>
    public WorkflowToolCatalogItem(ToolDefinition tool, bool isWhitelisted)
    {
        ArgumentNullException.ThrowIfNull(tool);
        Tool = tool;
        IsWhitelisted = isWhitelisted;
        SearchText = string.Join(
            ' ',
            [
                tool.Alias,
                tool.ToolGroup,
                tool.Description,
                .. tool.ArgumentDefinitions.Select(static argument => argument.Name),
            ]);
    }

    /// <summary>
    /// Gets the normative Tool Map entry.
    /// </summary>
    public ToolDefinition Tool { get; }

    /// <summary>
    /// Gets the workflow alias.
    /// </summary>
    public string Alias => Tool.Alias;

    /// <summary>
    /// Gets the owning group used for catalog grouping.
    /// </summary>
    public string ToolGroup => Tool.ToolGroup;

    /// <summary>
    /// Gets the entry description.
    /// </summary>
    public string Description => Tool.Description;

    /// <summary>
    /// Gets a value indicating whether the entry requires a sensitive task capability.
    /// </summary>
    public bool RequiresCapability => Tool.RequiredCapability is not null;

    /// <summary>
    /// Gets a value indicating whether the entry is a built-in AI step needing no tool group.
    /// </summary>
    public bool IsBuiltInAi => Tool.IsAiStep;

    /// <summary>
    /// Gets a value indicating whether the task currently grants the required tool group.
    /// </summary>
    /// <remarks>Built-in AI entries are never whitelistable and are always reported granted.</remarks>
    public bool IsWhitelisted { get; }

    /// <summary>
    /// Gets a value indicating whether a missing-whitelist lock must be displayed.
    /// </summary>
    public bool ShowsMissingWhitelistLock => !IsBuiltInAi && !IsWhitelisted;

    /// <summary>
    /// Gets the concatenated case-insensitive search corpus.
    /// </summary>
    public string SearchText { get; }

    /// <summary>
    /// Determines whether the entry matches a case-insensitive search term.
    /// </summary>
    /// <param name="term">The search term, which may be empty.</param>
    /// <returns><see langword="true"/> when the entry matches.</returns>
    public bool Matches(string? term) =>
        string.IsNullOrWhiteSpace(term) ||
        SearchText.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);
}
