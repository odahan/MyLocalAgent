namespace OD.MyLocalAgent.Core;

/// <summary>Describes compatibility between configured settings and the active Knowledge index.</summary>
public sealed record KnowledgeIndexStatus(
    bool HasIndex,
    bool RequiresReindex,
    string ConfiguredContract,
    string? IndexedContract,
    int? Dimension);
