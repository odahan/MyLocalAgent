namespace OD.MyLocalAgent.Core;

/// <summary>Describes one authorized Knowledge search result.</summary>
public sealed record KnowledgeSearchResult(
    int Rank,
    float Similarity,
    string DocumentId,
    string DocumentName,
    string Description,
    IReadOnlyList<string> Tags,
    string ChunkId,
    int ChunkOrdinal,
    string Text,
    string Citation,
    string? Section,
    int? PageNumber);
