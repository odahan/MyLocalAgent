namespace OD.MyLocalAgent.Core;

/// <summary>Describes one bounded authorized Knowledge search response.</summary>
public sealed record KnowledgeSearchResponse(
    string Status,
    IReadOnlyList<string> EffectiveTags,
    IReadOnlyList<KnowledgeSearchResult> Results,
    string? Message = null)
{
    /// <summary>Gets the number of returned excerpts.</summary>
    public int ResultCount => Results.Count;
}
