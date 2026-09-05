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

    /// <summary>Validates, deduplicates, and durably reserves one structured editorial selection.</summary>
    Task<EditorialReservationResult> ReserveSelectionAsync(
        string databasePath,
        string runId,
        string discoveryJson,
        string selectionJson,
        int lookbackMonths,
        double semanticSimilarityThreshold,
        CancellationToken cancellationToken);

    /// <summary>Builds a deterministic report from approved proposal blocks and marks it ready to export.</summary>
    Task<EditorialReportResult> PrepareReportAsync(
        string databasePath,
        string runId,
        string draftsJson,
        string reportTitle,
        CancellationToken cancellationToken);

    /// <summary>Confirms that the prepared immutable report was written unchanged.</summary>
    Task<EditorialExportResult> ConfirmExportAsync(
        string databasePath,
        string runId,
        string reportPath,
        CancellationToken cancellationToken);

    /// <summary>Inventories and optionally imports historical Markdown reports without changing their dates.</summary>
    Task<EditorialHistoryImportResult> ImportHistoryAsync(
        string databasePath,
        string reportsRoot,
        bool dryRun,
        CancellationToken cancellationToken);

    /// <summary>Inventories or rebuilds proposal embeddings with the configured local model.</summary>
    Task<EditorialEmbeddingRebuildResult> RebuildEmbeddingsAsync(
        string databasePath,
        bool dryRun,
        CancellationToken cancellationToken);
}

/// <summary>Describes proposal-memory schema initialization.</summary>
public sealed record ProposalDatabaseResult(bool DatabaseCreated, bool TableCreated, bool SchemaCompatible);

/// <summary>Describes one idempotent proposal write.</summary>
public sealed record ProposalStoreResult(bool Inserted, string ProposalKey, string EmbeddingModel, int EmbeddingDimension);

/// <summary>Represents the conservative novelty outcome for a selected subject.</summary>
public enum EditorialNoveltyDecision
{
    /// <summary>No matching historical or current-batch event was found.</summary>
    New,
    /// <summary>A related event exists, but distinct supported facts establish a material evolution.</summary>
    MaterialUpdate,
    /// <summary>The same event was already present in history or in the current batch.</summary>
    Duplicate,
    /// <summary>The available evidence cannot safely establish novelty.</summary>
    Uncertain,
}

/// <summary>Describes a source resolved from the protected discovery catalog.</summary>
public sealed record EditorialSource(string SourceId, string CanonicalUrl);

/// <summary>Describes one approved and durably reserved subject.</summary>
public sealed record ReservedEditorialProposal(
    string TemporaryId,
    long ProposalId,
    string Title,
    string FactualSummary,
    IReadOnlyList<EditorialSource> Sources,
    EditorialNoveltyDecision Decision);

/// <summary>Describes one rejected subject and its traceable novelty decision.</summary>
public sealed record RejectedEditorialProposal(
    string TemporaryId,
    EditorialNoveltyDecision Decision,
    string Reason,
    long? HistoricalProposalId,
    double? Similarity);

/// <summary>Contains the durable result of final novelty validation.</summary>
public sealed record EditorialReservationResult(
    string RunId,
    string Status,
    IReadOnlyList<ReservedEditorialProposal> Approved,
    IReadOnlyList<RejectedEditorialProposal> Rejected,
    string EmbeddingModel,
    int EmbeddingDimension,
    string ProjectionVersion,
    int IncompatibleEmbeddingCount);

/// <summary>Contains deterministic Markdown prepared for one reserved run.</summary>
public sealed record EditorialReportResult(string RunId, string Status, string Markdown, string Sha256, int SubjectCount);

/// <summary>Describes reconciliation of an exported report with its prepared fingerprint.</summary>
public sealed record EditorialExportResult(string RunId, string Status, string ReportPath, string Sha256);

/// <summary>Describes one historical subject reconciliation.</summary>
public sealed record EditorialHistoryImportItem(string ReportPath, DateOnly HistoricalDate, int SubjectOrdinal, string Title, string Status, long? ProposalId, string Reason);

/// <summary>Contains an idempotent historical-report import summary.</summary>
public sealed record EditorialHistoryImportResult(bool DryRun, int ReportsScanned, int SubjectsFound, int ExistingCount, int AmbiguousCount, int ImportedCount, IReadOnlyList<EditorialHistoryImportItem> Items);

/// <summary>Contains the result of an embedding reconstruction pass.</summary>
public sealed record EditorialEmbeddingRebuildResult(bool DryRun, int ConsideredCount, int RebuiltCount, string EmbeddingModel, int EmbeddingDimension, string ProjectionVersion);

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
