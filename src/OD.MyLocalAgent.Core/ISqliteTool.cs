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
}
