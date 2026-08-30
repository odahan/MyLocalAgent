namespace OD.MyLocalAgent.Core;

/// <summary>Owns proposal-memory persistence and deterministic candidate filtering.</summary>
public interface IProposalMemoryService
{
    /// <summary>Creates or validates the proposal-memory schema.</summary>
    Task<ProposalDatabaseResult> CreateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken);

    /// <summary>Stores one editorial proposal and its locally generated embedding.</summary>
    Task<ProposalStoreResult> StoreAsync(
        string databasePath,
        string title,
        string summary,
        string? sourceUrl,
        bool used,
        CancellationToken cancellationToken);

    /// <summary>Removes candidates matching applicable proposal-memory rows.</summary>
    Task<ProposalFilterResult> FilterAsync(
        string databasePath,
        IReadOnlyList<WebDocument> candidates,
        int lookbackMonths,
        bool useUsedTag,
        double semanticSimilarityThreshold,
        CancellationToken cancellationToken);
}

/// <summary>Describes proposal-memory schema initialization.</summary>
public sealed record ProposalDatabaseResult(bool DatabaseCreated, bool TableCreated, bool SchemaCompatible);

/// <summary>Describes one idempotent proposal write.</summary>
public sealed record ProposalStoreResult(bool Inserted, string ProposalKey, string EmbeddingModel, int EmbeddingDimension);

/// <summary>Contains filtered candidates and aggregate proposal-memory diagnostics.</summary>
public sealed record ProposalFilterResult(
    IReadOnlyList<WebDocument> Candidates,
    int ConsideredCount,
    int IncompatibleEmbeddingCount,
    int RejectedExactCount,
    int RejectedSemanticCount)
{
    /// <summary>Gets the total number of rejected candidates.</summary>
    public int RejectedCount => RejectedExactCount + RejectedSemanticCount;
}
