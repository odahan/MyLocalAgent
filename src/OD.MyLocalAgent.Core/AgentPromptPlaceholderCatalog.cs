using System.Collections.ObjectModel;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides the closed prompt-only placeholder catalog used by UI authoring help.
/// </summary>
public static class AgentPromptPlaceholderCatalog
{
    /// <summary>
    /// Gets the four supported prompt-only placeholder forms.
    /// </summary>
    public static IReadOnlyList<string> Forms { get; } =
        new ReadOnlyCollection<string>(
        [
            "{{Today}} or {{Today[custom .NET format]}}",
            "{{Now}} or {{Now[custom .NET format]}}",
            "{{task.id}}",
            "{{task.name}}",
        ]);

    /// <summary>
    /// Gets a concise UI help projection from the shared catalog.
    /// </summary>
    public static string HelpText { get; } =
        $"Available: {string.Join(" · ", Forms)}. Example: Sort-{{{{Today[yyyyMMdd]}}}}.md";
}
