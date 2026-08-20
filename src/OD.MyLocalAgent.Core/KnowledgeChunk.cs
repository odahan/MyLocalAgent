namespace OD.MyLocalAgent.Core;

/// <summary>Describes one deterministic searchable chunk.</summary>
public sealed record KnowledgeChunk(
    string Id,
    int Ordinal,
    string Text,
    int EstimatedTokens,
    int StartCharacter,
    int EndCharacter,
    string? Heading,
    int? PageNumber);
