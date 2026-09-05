using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Provides bounded operations on one authorized local SQLite database per call.</summary>
public interface ISqliteTool
{
    /// <summary>Lists bounded schema-object metadata.</summary>
    Task<string> ListObjectsAsync(string databasePath, SQLiteObjectType objectType = SQLiteObjectType.All, string? namePattern = null, int? maxResults = null, CancellationToken cancellationToken = default);
    /// <summary>Describes a table or view without executing its schema text.</summary>
    Task<string> DescribeTableAsync(string databasePath, string tableName, CancellationToken cancellationToken = default);
    /// <summary>Runs one bounded read-only statement.</summary>
    Task<string> QueryAsync(string? databasePath, string sql, JsonElement? parameters = null, int? maxRows = null, string? storageSpaceId = null, CancellationToken cancellationToken = default);
    /// <summary>Runs one modifying statement in an application-owned transaction.</summary>
    Task<string> ExecuteAsync(string databasePath, string sql, JsonElement? parameters = null, bool createIfMissing = false, CancellationToken cancellationToken = default);
    /// <summary>Creates or validates a task-owned editorial proposal-memory database.</summary>
    Task<string> CreateProposalDatabaseAsync(string databasePath, CancellationToken cancellationToken = default);
    /// <summary>Stores one editorial proposal with a locally generated embedding.</summary>
    Task<string> StoreProposalAsync(string databasePath, string title, string summary, string? sourceUrl = null, bool used = false, CancellationToken cancellationToken = default);
    /// <summary>Validates final structured novelty and reserves approved editorial proposals.</summary>
    Task<string> ReserveEditorialSelectionAsync(string databasePath, string runId, string discoveryJson, string selectionJson, int lookbackMonths = 6, double semanticSimilarityThreshold = 0.92, CancellationToken cancellationToken = default);
    /// <summary>Assembles approved draft blocks into deterministic Markdown and marks the run ready.</summary>
    Task<string> PrepareEditorialReportAsync(string databasePath, string runId, string draftsJson, string reportTitle, CancellationToken cancellationToken = default);
    /// <summary>Confirms that an exported report matches the prepared immutable content.</summary>
    Task<string> ConfirmEditorialExportAsync(string databasePath, string runId, string reportPath, CancellationToken cancellationToken = default);
    /// <summary>Inventories or imports historical editorial Markdown reports.</summary>
    Task<string> ImportEditorialHistoryAsync(string databasePath, string reportsRoot, bool dryRun = true, CancellationToken cancellationToken = default);
    /// <summary>Inventories or rebuilds editorial proposal embeddings.</summary>
    Task<string> RebuildEditorialEmbeddingsAsync(string databasePath, bool dryRun = true, CancellationToken cancellationToken = default);
}
