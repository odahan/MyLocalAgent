namespace OD.MyLocalAgent.Core;

/// <summary>Describes one imported Knowledge document.</summary>
public sealed record KnowledgeDocument(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    string OriginalFileName,
    string MediaType,
    long SizeBytes,
    int CharacterCount,
    int ChunkCount,
    DateTimeOffset UpdatedUtc,
    string Status = "Ready",
    int? PageCount = null);
