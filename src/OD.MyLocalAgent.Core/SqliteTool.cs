using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

/// <summary>Implements bounded and path-authorized SQLite operations.</summary>
[ToolGroup("sqlite", "SQLite", "Inspect and update authorized local SQLite databases.", 500, Activation = ToolActivationKind.Contextual, ServiceContract = typeof(ISqliteTool))]
public sealed class SqliteTool : ISqliteTool
{
    private static readonly HashSet<string> ForbiddenTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "attach", "detach", "pragma", "vacuum", "load_extension", "begin", "commit", "end", "rollback", "savepoint", "release", "reindex", "analyze", "temp", "temporary", "virtual", "sqlite_master", "sqlite_schema", "sqlite_temp_schema", "sqlite_sequence",
    };

    private static readonly HashSet<string> ModifyingTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "insert", "update", "delete", "replace", "create", "drop", "alter",
    };

    private readonly IPathSandbox _sandbox;
    private readonly IOptions<SQLiteOptions> _options;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IContentTrustService _contentTrustService;
    private readonly ToolExecutionContext _context;
    private readonly ILogger<SqliteTool> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IIncidentReporter _incidentReporter;

    static SqliteTool()
    {
        SqliteNativeBootstrap.Initialize();
    }

    /// <summary>Initializes a new instance of the <see cref="SqliteTool"/> class.</summary>
    [ActivatorUtilitiesConstructor]
    public SqliteTool(IPathSandbox sandbox, IOptions<SQLiteOptions> options, IOptionsMonitor<GuardrailsOptions> guardrailsOptions, IContentTrustService contentTrustService, ToolExecutionContext context, ILogger<SqliteTool> logger, TimeProvider timeProvider, IIncidentReporter incidentReporter)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _guardrailsOptions = guardrailsOptions ?? throw new ArgumentNullException(nameof(guardrailsOptions));
        _contentTrustService = contentTrustService ?? throw new ArgumentNullException(nameof(contentTrustService));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _incidentReporter = incidentReporter ?? throw new ArgumentNullException(nameof(incidentReporter));
    }

    /// <inheritdoc />
    [ToolLocalPath("databasePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("sqlite.listObjects", "Lists bounded SQLite schema objects in deterministic order.", 100)]
    public Task<string> ListObjectsAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized SQLite database file path.", Example = "data/app.db")] string databasePath,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Optional schema object type.", Example = "Table")] SQLiteObjectType objectType = SQLiteObjectType.All,
        [ToolArgument(WorkflowArgumentKind.String, "Optional SQLite LIKE name pattern.", Example = "item%")] string? namePattern = null,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional bounded schema object count.", Example = "100")] int? maxResults = null,
        CancellationToken cancellationToken = default) => ExecuteSanitizedAsync("sqlite.listObjects", "read", maxResults, Task.Run(async () =>
    {
        var limit = Limit(maxResults);
        using var connection = OpenRead(databasePath);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, type, tbl_name FROM sqlite_schema WHERE name NOT LIKE 'sqlite_%' AND type IN ('table','view','index','trigger') AND ($type = 'all' OR type = $type) AND ($pattern IS NULL OR name LIKE $pattern) ORDER BY CASE type WHEN 'table' THEN 1 WHEN 'view' THEN 2 WHEN 'index' THEN 3 ELSE 4 END, name COLLATE BINARY LIMIT $limit";
        command.Parameters.AddWithValue("$type", objectType == SQLiteObjectType.All ? "all" : objectType.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$pattern", (object?)namePattern ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit + 1);
        using var reader = command.ExecuteReader();
        var objects = new List<object>();
        var resultTruncated = false;
        while (reader.Read() && objects.Count <= limit)
        {
            objects.Add(new { name = reader.GetString(0), type = reader.GetString(1), tableName = reader.GetString(2) });
            if (ToolJson.Serialize(new
            {
                objects,
                hasMoreObjects = true,
                truncated = true,
                resultTruncated = true,
            }).Length > _options.Value.MaxResultCharacters)
            {
                objects.RemoveAt(objects.Count - 1);
                resultTruncated = true;
                break;
            }
        }
        var exceededItemLimit = objects.Count > limit;
        var hasMore = resultTruncated || exceededItemLimit;
        if (exceededItemLimit) objects.RemoveAt(objects.Count - 1);
        return await DeliverReadResultAsync(ToolJson.Serialize(new { objects, hasMoreObjects = hasMore, truncated = hasMore || resultTruncated, resultTruncated }), cancellationToken).ConfigureAwait(false);
    }, cancellationToken), cancellationToken);

    /// <inheritdoc />
    [ToolLocalPath("databasePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("sqlite.describeTable", "Describes bounded columns, foreign keys, and indexes for one SQLite table or view.", 200)]
    public Task<string> DescribeTableAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized SQLite database file path.", Example = "data/app.db")] string databasePath,
        [ToolArgument(WorkflowArgumentKind.String, "Exact table or view name.", Example = "items")] string tableName,
        CancellationToken cancellationToken = default) => ExecuteSanitizedAsync("sqlite.describeTable", "read", null, Task.Run(async () =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        if (tableName.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SQLite internal schema objects cannot be described.");
        }

        using var connection = OpenRead(databasePath);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_schema WHERE type IN ('table', 'view') AND name = $name LIMIT 1";
            exists.Parameters.AddWithValue("$name", tableName);
            if (exists.ExecuteScalar() is null)
            {
                throw new InvalidOperationException("The requested SQLite table or view does not exist.");
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_xinfo({QuoteIdentifier(tableName)})";
        using var reader = command.ExecuteReader();
        var columns = new List<object>();
        while (reader.Read() && columns.Count < _options.Value.MaxRows)
        {
            columns.Add(new { ordinal = reader.GetInt32(0), name = reader.GetString(1), declaredType = reader.IsDBNull(2) ? null : reader.GetString(2), notNull = reader.GetInt32(3) != 0, defaultExpression = reader.IsDBNull(4) ? null : reader.GetString(4), primaryKeyOrder = reader.GetInt32(5), hidden = reader.GetInt32(6) });
        }
        reader.Dispose();
        var foreignKeys = new List<object>();
        using (var foreignKeyCommand = connection.CreateCommand())
        {
            foreignKeyCommand.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)})";
            using var foreignKeyReader = foreignKeyCommand.ExecuteReader();
            while (foreignKeyReader.Read() && foreignKeys.Count < _options.Value.MaxRows)
            {
                foreignKeys.Add(new
                {
                    id = foreignKeyReader.GetInt32(0),
                    sequence = foreignKeyReader.GetInt32(1),
                    referencedTable = foreignKeyReader.GetString(2),
                    fromColumn = foreignKeyReader.GetString(3),
                    toColumn = foreignKeyReader.IsDBNull(4) ? null : foreignKeyReader.GetString(4),
                    onUpdate = foreignKeyReader.GetString(5),
                    onDelete = foreignKeyReader.GetString(6),
                });
            }
        }

        var indexes = new List<object>();
        using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText = $"PRAGMA index_list({QuoteIdentifier(tableName)})";
            using var indexReader = indexCommand.ExecuteReader();
            while (indexReader.Read() && indexes.Count < _options.Value.MaxRows)
            {
                indexes.Add(new
                {
                    sequence = indexReader.GetInt32(0),
                    name = indexReader.GetString(1),
                    unique = indexReader.GetInt32(2) != 0,
                    origin = indexReader.IsDBNull(3) ? null : indexReader.GetString(3),
                    partial = !indexReader.IsDBNull(4) && indexReader.GetInt32(4) != 0,
                });
            }
        }

        var resultTruncated = false;
        string? deliveredTableName = tableName;
        while (true)
        {
            var bounded = ToolJson.Serialize(new
            {
                tableName = deliveredTableName,
                columns,
                foreignKeys,
                indexes,
                truncated = resultTruncated ||
                    columns.Count == _options.Value.MaxRows ||
                    foreignKeys.Count == _options.Value.MaxRows ||
                    indexes.Count == _options.Value.MaxRows,
                resultTruncated,
            });
            if (bounded.Length <= _options.Value.MaxResultCharacters)
            {
                return await DeliverReadResultAsync(bounded, cancellationToken).ConfigureAwait(false);
            }

            resultTruncated = true;
            if (indexes.Count != 0)
            {
                indexes.RemoveAt(indexes.Count - 1);
            }
            else if (foreignKeys.Count != 0)
            {
                foreignKeys.RemoveAt(foreignKeys.Count - 1);
            }
            else if (columns.Count != 0)
            {
                columns.RemoveAt(columns.Count - 1);
            }
            else
            {
                deliveredTableName = null;
            }
        }
    }, cancellationToken), cancellationToken);

    /// <inheritdoc />
    [ToolLocalPath("databasePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("sqlite.query", "Executes one read-only SQLite statement and returns bounded ordered rows.", 300)]
    public Task<string> QueryAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized SQLite database file path.", Example = "data/app.db")] string? databasePath,
        [ToolArgument(WorkflowArgumentKind.String, "One read-only SQLite statement.", Example = "SELECT id, name FROM items")] string sql,
        [ToolArgument(WorkflowArgumentKind.Object, "Optional named scalar SQLite parameters.", Example = "{}")] JsonElement parameters = default,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional bounded row count.", Example = "100")] int? maxRows = null,
        [ToolArgument(WorkflowArgumentKind.String, "Reserved managed storage target identifier.", Example = "research", IsExcludedFromWorkflowTextEditor = true)] string? storageSpaceId = null,
        CancellationToken cancellationToken = default) => ExecuteSanitizedAsync("sqlite.query", "read", maxRows, Task.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !string.IsNullOrWhiteSpace(storageSpaceId)) throw new InvalidOperationException("Exactly one database path target is required.");
        using var connection = OpenRead(databasePath);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        var parameterNames = Inspect(connection, sql, readOnly: true);
        using var command = CreateCommand(connection, sql, parameters, parameterNames);
        using var reader = command.ExecuteReader();
        if (reader.FieldCount == 0) throw new SqliteToolException(SqliteToolErrorCategory.InvalidStatement);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(i => new { name = reader.GetName(i), ordinal = i, declaredType = reader.GetDataTypeName(i) }).ToArray();
        var rows = new List<object?[]>();
        var limit = Limit(maxRows);
        var hasMore = false;
        var resultTruncated = false;
        while (reader.Read())
        {
            if (rows.Count >= limit)
            {
                hasMore = true;
                break;
            }

            var row = Enumerable.Range(0, reader.FieldCount)
                .Select(i => Value(reader, i))
                .ToArray();
            rows.Add(row);
            var candidate = ToolJson.Serialize(new { columns, rows, hasMoreRows = true, truncated = true, resultTruncated = true });
            if (candidate.Length > _options.Value.MaxResultCharacters)
            {
                rows.RemoveAt(rows.Count - 1);
                hasMore = true;
                resultTruncated = true;
                break;
            }
        }

        var bounded = ToolJson.Serialize(new
        {
            columns,
            rows,
            hasMoreRows = hasMore,
            truncated = hasMore || resultTruncated,
            resultTruncated,
        });
        return await DeliverReadResultAsync(bounded, cancellationToken).ConfigureAwait(false);
    }, cancellationToken), cancellationToken);

    /// <inheritdoc />
    [ToolLocalPath("databasePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]
    [Tool("sqlite.execute", "Executes one modifying SQLite statement atomically and may create a database only when explicitly requested.", 400, RequiredCapability = TaskCapability.SqliteWrite)]
    public Task<string> ExecuteAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized SQLite database file path.", Example = "data/app.db")] string databasePath,
        [ToolArgument(WorkflowArgumentKind.String, "One modifying SQLite statement.", Example = "UPDATE items SET name = @name WHERE id = @id")] string sql,
        [ToolArgument(WorkflowArgumentKind.Object, "Optional named scalar SQLite parameters.", Example = "{}")] JsonElement parameters = default,
        [ToolArgument(WorkflowArgumentKind.Boolean, "Whether a missing database file may be created.", Example = "false")] bool createIfMissing = false,
        CancellationToken cancellationToken = default) => ExecuteSanitizedAsync("sqlite.execute", createIfMissing ? "create" : "write", null, Task.Run(() =>
    {
        ValidateDatabasePathInput(databasePath);
        var path = _sandbox.Resolve(databasePath, FileSystemAccessMode.ReadWrite, _context);
        if (Directory.Exists(path)) throw new InvalidOperationException("The SQLite database path must identify a file.");
        var existed = File.Exists(path);
        if (!existed && !createIfMissing) throw new FileNotFoundException("The SQLite database file does not exist.");
        if (!existed && !Directory.Exists(Path.GetDirectoryName(path))) throw new DirectoryNotFoundException("The authorized database parent directory does not exist.");
        var createdByThisCall = false;
        var journalExistedBeforeCall = File.Exists(path + "-journal");
        var writeAheadLogExistedBeforeCall = File.Exists(path + "-wal");
        var sharedMemoryExistedBeforeCall = File.Exists(path + "-shm");
        if (!existed)
        {
            try
            {
                using var claim = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);
                createdByThisCall = true;
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    "The SQLite database file could not be created exclusively.",
                    exception);
            }
        }

        try
        {
            using var connection = Open(path, SqliteOpenMode.ReadWrite);
            using var interrupt = RegisterInterrupt(connection, cancellationToken);
            var parameterNames = Inspect(connection, sql, readOnly: false);
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            using var command = CreateCommand(connection, sql, parameters, parameterNames);
            command.Transaction = transaction;
            using var reader = command.ExecuteReader();
            if (reader.FieldCount != 0)
            {
                throw new SqliteToolException(SqliteToolErrorCategory.InvalidStatement);
            }

            var affected = reader.RecordsAffected;
            transaction.Commit();
            using var rowIdCommand = connection.CreateCommand();
            rowIdCommand.CommandText = "SELECT last_insert_rowid()";
            var lastInsertRowId = (long)(rowIdCommand.ExecuteScalar() ?? 0L);
            return ToolJson.Serialize(new { rowsAffected = affected, lastInsertRowId, databaseCreated = createdByThisCall, committed = true });
        }
        catch
        {
            if (createdByThisCall)
            {
                DeleteOwnedDatabaseArtifacts(
                    path,
                    journalExistedBeforeCall,
                    writeAheadLogExistedBeforeCall,
                    sharedMemoryExistedBeforeCall);
            }

            throw;
        }
    }, cancellationToken), cancellationToken);

    private SqliteConnection OpenRead(string databasePath)
    {
        ValidateDatabasePathInput(databasePath);
        var path = _sandbox.Resolve(databasePath, FileSystemAccessMode.ReadOnly, _context);
        if (!File.Exists(path)) throw new FileNotFoundException("The SQLite database file does not exist.");
        if (Directory.Exists(path)) throw new InvalidOperationException("The SQLite database path must identify a file.");
        return Open(path, SqliteOpenMode.ReadOnly);
    }

    private static void DeleteOwnedDatabaseArtifacts(
        string path,
        bool journalExistedBeforeCall,
        bool writeAheadLogExistedBeforeCall,
        bool sharedMemoryExistedBeforeCall)
    {
        DeleteIfPresent(path);
        if (!journalExistedBeforeCall) DeleteIfPresent(path + "-journal");
        if (!writeAheadLogExistedBeforeCall) DeleteIfPresent(path + "-wal");
        if (!sharedMemoryExistedBeforeCall) DeleteIfPresent(path + "-shm");
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void ValidateDatabasePathInput(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var value = databasePath.Trim();
        if (value.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("\\\\.\\", StringComparison.Ordinal) ||
            value.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SQLite database paths must identify an authorized local file.");
        }
    }

    private static CancellationTokenRegistration RegisterInterrupt(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        cancellationToken.Register(
            static state => SQLitePCL.raw.sqlite3_interrupt(((SqliteConnection)state!).Handle),
            connection);

    /// <summary>
    /// Wraps one alias execution with the content-free sanitized log path required by Stage 19.
    /// Only counters, categories, numeric SQLite codes, durations, outcome, and a correlation id
    /// are emitted; SQL, paths, identifiers, values, and raw exception messages never are.
    /// </summary>
    private async Task<string> ExecuteSanitizedAsync(
        string alias,
        string classification,
        int? requestedCap,
        Task<string> operation,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var started = _timeProvider.GetTimestamp();
        var guardrailsEnabled = _context.SqliteGuardrailsEnabled ?? _options.Value.GuardrailsEnabledByDefault;
        try
        {
            var result = await NormalizeCancellationAsync(operation, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "SQLite {SqliteAlias} ({SqliteClassification}) completed in {DurationMs} ms with outcome {Outcome}; requested cap {RequestedCap}, result characters {ResultCharacterCount}, guardrails enabled {SqliteGuardrailsEnabled}, correlation {CorrelationId}.",
                alias,
                classification,
                _timeProvider.GetElapsedTime(started).TotalMilliseconds,
                "Success",
                requestedCap,
                result.Length,
                guardrailsEnabled,
                correlationId);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "SQLite {SqliteAlias} ({SqliteClassification}) completed in {DurationMs} ms with outcome {Outcome}; correlation {CorrelationId}.",
                alias,
                classification,
                _timeProvider.GetElapsedTime(started).TotalMilliseconds,
                "Canceled",
                correlationId);
            throw;
        }
        catch (SqliteToolException exception)
        {
            _logger.LogWarning(
                "SQLite {SqliteAlias} ({SqliteClassification}) completed in {DurationMs} ms with outcome {Outcome}; category {SqliteErrorCategory}, primary code {SqlitePrimaryCode}, extended code {SqliteExtendedCode}, correlation {CorrelationId}.",
                alias,
                classification,
                _timeProvider.GetElapsedTime(started).TotalMilliseconds,
                "Failure",
                exception.Category,
                exception.PrimaryErrorCode,
                exception.ExtendedErrorCode,
                correlationId);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "SQLite {SqliteAlias} ({SqliteClassification}) completed in {DurationMs} ms with outcome {Outcome}; exception type {ExceptionType}, correlation {CorrelationId}.",
                alias,
                classification,
                _timeProvider.GetElapsedTime(started).TotalMilliseconds,
                "Failure",
                exception.GetType().Name,
                correlationId);
            throw;
        }
    }

    private static async Task<string> NormalizeCancellationAsync(
        Task<string> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation.ConfigureAwait(false);
        }
        catch (SqliteException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (SqliteException exception)
        {
            throw SqliteToolException.FromSqliteError(exception);
        }
        catch (SqliteToolException)
        {
            throw;
        }
        catch (SafetyException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("SQLite operation failed.");
        }
    }

    private SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = _options.Value.CommandTimeoutSeconds }.ToString());
        connection.Open();
        if (mode != SqliteOpenMode.ReadOnly)
        {
            using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON";
            foreignKeys.ExecuteNonQuery();
            foreignKeys.CommandText = "PRAGMA foreign_keys";
            if (Convert.ToInt64(foreignKeys.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 1)
            {
                connection.Dispose();
                throw new InvalidOperationException("SQLite foreign-key enforcement could not be enabled.");
            }
        }

        return connection;
    }

    private SqliteCommand CreateCommand(
        SqliteConnection connection,
        string sql,
        JsonElement parameters,
        IReadOnlyCollection<string> requiredParameterNames)
    {
        var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = _options.Value.CommandTimeoutSeconds;
        if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            if (requiredParameterNames.Count != 0)
            {
                throw new SqliteToolException(SqliteToolErrorCategory.ParameterMismatch);
            }

            return command;
        }

        if (parameters.ValueKind != JsonValueKind.Object) throw new SqliteToolException(SqliteToolErrorCategory.ParameterMismatch);
        var suppliedParameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters.EnumerateObject())
        {
            if (!suppliedParameterNames.Add(parameter.Name) || !requiredParameterNames.Contains(parameter.Name, StringComparer.Ordinal))
            {
                throw new SqliteToolException(SqliteToolErrorCategory.ParameterMismatch);
            }

            if (parameter.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) throw new SqliteToolException(SqliteToolErrorCategory.ParameterMismatch);
            command.Parameters.AddWithValue(parameter.Name, parameter.Value.ValueKind switch { JsonValueKind.Null => DBNull.Value, JsonValueKind.String => parameter.Value.GetString()!, JsonValueKind.True => 1, JsonValueKind.False => 0, JsonValueKind.Number when parameter.Value.TryGetInt64(out var integer) => integer, JsonValueKind.Number => parameter.Value.GetDouble(), _ => throw new SqliteToolException(SqliteToolErrorCategory.ParameterMismatch) });
        }

        if (suppliedParameterNames.Count != requiredParameterNames.Count)
        {
            throw new SqliteToolException(SqliteToolErrorCategory.ParameterMismatch);
        }

        return command;
    }

    private int Limit(int? requested)
    {
        if (requested is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requested), "SQLite result limits must be at least one.");
        }

        return Math.Min(requested ?? _options.Value.DefaultMaxRows, _options.Value.MaxRows);
    }

    private async Task<string> DeliverReadResultAsync(string result, CancellationToken cancellationToken)
    {
        const int maximumScreeningChunks = 36;
        var enabled = _context.SqliteGuardrailsEnabled ?? _options.Value.GuardrailsEnabledByDefault;
        using var document = JsonDocument.Parse(result);
        var data = document.RootElement.Clone();
        if (!enabled)
        {
            return ReadEnvelope(data, null, "PlainText", screened: false, sqliteGuardrailsEnabled: false, null, null, requiresHumanReview: false, screeningChunkCount: 0, result.Length);
        }

        var guardrails = _guardrailsOptions.CurrentValue;
        if (!guardrails.ScreeningEnabled)
        {
            return ReadEnvelope(null, ContentTrustEnvelope.WrapBase64(result), "Base64", screened: false, sqliteGuardrailsEnabled: true, null, "ScreeningUnavailable", requiresHumanReview: true, screeningChunkCount: 0, result.Length);
        }

        var score = 100;
        var screeningChunkCount = 0;
        try
        {
            var chunks = SplitForScreening(result, guardrails.MaxContentChars).ToArray();
            if (chunks.Length > maximumScreeningChunks)
            {
                return ReadEnvelope(null, ContentTrustEnvelope.WrapBase64(result), "Base64", screened: false, sqliteGuardrailsEnabled: true, null, "ScreeningUnavailable", requiresHumanReview: true, screeningChunkCount: 0, result.Length);
            }

            var items = chunks
                .Select(static (chunk, index) => new CompactExternalContentItem($"sqlite-chunk-{index:D2}-{Guid.NewGuid():N}", chunk, 1))
                .ToArray();
            var batch = await _contentTrustService
                .ScreenCompactAsync(items, cancellationToken)
                .ConfigureAwait(false);
            screeningChunkCount = chunks.Length;
            if (batch.Failures.Count > 0 || batch.Items.Count != items.Length)
            {
                foreach (var failure in batch.Failures)
                {
                    await _incidentReporter
                        .ReportAsync(failure.Exception, "SqliteTool.Guardrails", cancellationToken)
                        .ConfigureAwait(false);
                }

                return ReadEnvelope(null, ContentTrustEnvelope.WrapBase64(result), "Base64", screened: false, sqliteGuardrailsEnabled: true, null, "ScreeningUnavailable", requiresHumanReview: true, screeningChunkCount, result.Length);
            }

            for (var index = 0; index < items.Length; index++)
            {
                var item = batch.Items[index];
                if (!string.Equals(item.Id, items[index].Id, StringComparison.Ordinal) ||
                    item.Status is not (CompactExternalContentStatus.Screened or CompactExternalContentStatus.SourceWithheld) ||
                    item.SourceTrust.TrustScore is null)
                {
                    return ReadEnvelope(null, ContentTrustEnvelope.WrapBase64(result), "Base64", screened: false, sqliteGuardrailsEnabled: true, null, "ScreeningUnavailable", requiresHumanReview: true, screeningChunkCount, result.Length);
                }

                score = Math.Min(score, item.SourceTrust.TrustScore.Value);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await _incidentReporter
                .ReportAsync(
                    new InvalidOperationException("SQLite Guardrails screening failed unexpectedly."),
                    "SqliteTool.Guardrails",
                    cancellationToken)
                .ConfigureAwait(false);
            return ReadEnvelope(null, ContentTrustEnvelope.WrapBase64(result), "Base64", screened: false, sqliteGuardrailsEnabled: true, null, "ScreeningUnavailable", requiresHumanReview: true, screeningChunkCount, result.Length);
        }

        if (score < guardrails.CautionThreshold)
        {
            return ReadEnvelope(null, "[SQLite result withheld by guardrails.]", "None", screened: true, sqliteGuardrailsEnabled: true, score, "Withheld", requiresHumanReview: true, screeningChunkCount, result.Length);
        }

        return score >= guardrails.CleanThreshold
            ? ReadEnvelope(data, null, "PlainText", screened: true, sqliteGuardrailsEnabled: true, score, "Clean", requiresHumanReview: false, screeningChunkCount, result.Length)
            : ReadEnvelope(null, ContentTrustEnvelope.WrapBase64(result), "Base64", screened: true, sqliteGuardrailsEnabled: true, score, "Caution", requiresHumanReview: true, screeningChunkCount, result.Length);
    }

    private static string ReadEnvelope(
        JsonElement? data,
        string? protectedData,
        string contentEncoding,
        bool screened,
        bool sqliteGuardrailsEnabled,
        int? trustScore,
        string? trustStatus,
        bool requiresHumanReview,
        int screeningChunkCount,
        int resultCharacterCount) =>
        ToolJson.Serialize(new
        {
            data,
            protectedData,
            contentEncoding,
            screened,
            sqliteGuardrailsEnabled,
            trustScore,
            trustStatus,
            requiresHumanReview,
            screeningChunkCount,
            resultCharacterCount,
        });

    private static IEnumerable<string> SplitForScreening(string value, int maximumLength)
    {
        for (var offset = 0; offset < value.Length;)
        {
            var length = Math.Min(maximumLength, value.Length - offset);
            if (offset + length < value.Length &&
                char.IsHighSurrogate(value[offset + length - 1]) &&
                char.IsLowSurrogate(value[offset + length]))
            {
                length--;
            }

            yield return value.Substring(offset, length);
            offset += length;
        }
    }
    private object? Value(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        if (reader.GetValue(ordinal) is not byte[] blob)
        {
            return reader.GetValue(ordinal);
        }

        var omitted = _options.Value.MaxBlobBytes == 0 || blob.Length > _options.Value.MaxBlobBytes;
        return new Dictionary<string, object?>
        {
            ["$type"] = "blob",
            ["byteLength"] = blob.Length,
            ["base64"] = omitted ? null : Convert.ToBase64String(blob),
            ["omitted"] = omitted,
        };
    }
    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private IReadOnlyCollection<string> Inspect(SqliteConnection connection, string sql, bool readOnly)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > _options.Value.MaxSqlCharacters) throw new SqliteToolException(SqliteToolErrorCategory.InvalidStatement);
        var words = SqlWords(sql, out var statements);
        if (statements != 1 || words.Any(ForbiddenTokens.Contains)) throw new SqliteToolException(SqliteToolErrorCategory.ForbiddenFeature);
        var first = words.FirstOrDefault() ?? string.Empty;
        if (readOnly && (first is not ("select" or "with" or "values" or "explain") || words.Any(ModifyingTokens.Contains))) throw new SqliteToolException(SqliteToolErrorCategory.InvalidStatement);
        if (!readOnly && (first is not ("insert" or "update" or "delete" or "replace" or "create" or "drop" or "alter") || words.Contains("returning", StringComparer.OrdinalIgnoreCase))) throw new SqliteToolException(SqliteToolErrorCategory.InvalidStatement);
        var result = SQLitePCL.raw.sqlite3_prepare_v2(
            connection.Handle,
            sql,
            out SQLitePCL.sqlite3_stmt statement,
            out string tail);
        if (result != SQLitePCL.raw.SQLITE_OK || statement is null)
        {
            throw new SqliteToolException(SqliteToolErrorCategory.InvalidStatement, result);
        }

        using (statement)
        {
            _ = SqlWords(tail, out var tailStatements);
            if (tailStatements != 0)
            {
                throw new SqliteToolException(SqliteToolErrorCategory.ForbiddenFeature);
            }

            var nativeReadOnly = SQLitePCL.raw.sqlite3_stmt_readonly(statement) != 0;
            var columnCount = SQLitePCL.raw.sqlite3_column_count(statement);
            if (readOnly && (!nativeReadOnly || columnCount == 0))
            {
                throw new SqliteToolException(SqliteToolErrorCategory.InvalidStatement);
            }

            if (!readOnly && (nativeReadOnly || columnCount != 0))
            {
                throw new SqliteToolException(SqliteToolErrorCategory.InvalidStatement);
            }

            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            var parameterCount = SQLitePCL.raw.sqlite3_bind_parameter_count(statement);
            for (var index = 1; index <= parameterCount; index++)
            {
                var parameterName = SQLitePCL.raw.sqlite3_bind_parameter_name(statement, index).utf8_to_string();
                if (string.IsNullOrEmpty(parameterName) || parameterName[0] is not ('@' or ':' or '$'))
                {
                    throw new SqliteToolException(SqliteToolErrorCategory.ParameterMismatch);
                }

                parameterNames.Add(parameterName);
            }

            return parameterNames;
        }
    }

    private static IReadOnlyList<string> SqlWords(string sql, out int statements)
    {
        var words = new List<string>(); var token = new System.Text.StringBuilder(); statements = 0; var hasExecutableToken = false; char quote = '\0';
        for (var i = 0; i < sql.Length; i++) { var c = sql[i]; if (quote != '\0') { if (c == quote) { if (i + 1 < sql.Length && sql[i + 1] == quote) i++; else quote = '\0'; } continue; } if (c is '\'' or '\"' or '`') { Flush(); hasExecutableToken = true; quote = c; continue; } if (c == '[') { Flush(); hasExecutableToken = true; while (++i < sql.Length && sql[i] != ']') { } continue; } if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-') { Flush(); while (i < sql.Length && sql[i] != '\n') i++; continue; } if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { Flush(); i += 2; while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++; i++; continue; } if (c == ';') { Flush(); if (hasExecutableToken) { statements++; hasExecutableToken = false; } continue; } if (!char.IsWhiteSpace(c)) hasExecutableToken = true; if (char.IsLetter(c) || c == '_') token.Append(char.ToLowerInvariant(c)); else Flush(); }
        Flush(); if (hasExecutableToken) statements++; return words;
        void Flush() { if (token.Length > 0) { words.Add(token.ToString()); token.Clear(); } }
    }
}
