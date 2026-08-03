using System.Collections.ObjectModel;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents one pure prompt-placeholder expansion.
/// </summary>
public sealed class AgentPromptPlaceholderResult
{
    private readonly IReadOnlyDictionary<AgentPromptPlaceholderWarningKind, int> _counts;

    internal AgentPromptPlaceholderResult(
        string resolvedPrompt,
        IReadOnlyList<AgentPromptPlaceholderOccurrence> occurrences)
    {
        ResolvedPrompt = resolvedPrompt;
        UnresolvedOccurrences = Array.AsReadOnly([.. occurrences]);
        _counts = new ReadOnlyDictionary<AgentPromptPlaceholderWarningKind, int>(
            occurrences
                .GroupBy(static occurrence => occurrence.Kind)
                .ToDictionary(static group => group.Key, static group => group.Count()));
    }

    /// <summary>
    /// Gets the prompt after supported occurrences were replaced once.
    /// </summary>
    public string ResolvedPrompt { get; }

    /// <summary>
    /// Gets unresolved occurrences in source order without their source text.
    /// </summary>
    public IReadOnlyList<AgentPromptPlaceholderOccurrence> UnresolvedOccurrences { get; }

    /// <summary>
    /// Gets the total unresolved occurrence count.
    /// </summary>
    public int TotalUnresolvedCount => UnresolvedOccurrences.Count;

    /// <summary>
    /// Gets immutable aggregate counts for warning kinds that occurred.
    /// </summary>
    public IReadOnlyDictionary<AgentPromptPlaceholderWarningKind, int> Counts => _counts;

    /// <summary>
    /// Gets the occurrence count for one warning kind.
    /// </summary>
    /// <param name="kind">The warning kind to count.</param>
    /// <returns>The exact occurrence count.</returns>
    public int GetCount(AgentPromptPlaceholderWarningKind kind) =>
        _counts.TryGetValue(kind, out var count) ? count : 0;
}
