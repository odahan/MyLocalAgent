namespace OD.MyLocalAgent.Core;

/// <summary>Describes one bounded administrative preview of an active Knowledge chunk.</summary>
public sealed record KnowledgeChunkPreview(
    int Ordinal,
    string Text,
    int EstimatedTokens,
    string? Heading,
    int? PageNumber);
