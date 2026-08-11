using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the bounded local SQLite tool against an isolated temporary database.
/// </summary>
public sealed class SqliteToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"od-sqlite-{Guid.NewGuid():N}");

    /// <summary>
    /// Creates a database, writes parameterized data, and reads ordered rows.
    /// </summary>
    [Fact]
    public async Task ExecuteAndQueryUseTheWindowsServicedSqliteRuntime()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);

        await tool.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT)", createIfMissing: true);
        using var parameters = JsonDocument.Parse("{\"@name\":\"canary\"}");
        await tool.ExecuteAsync("data.db", "INSERT INTO items (name) VALUES (@name)", parameters.RootElement);
        var json = await tool.QueryAsync("data.db", "SELECT id, name FROM items ORDER BY id");

        using var result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("screened").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("data").GetProperty("columns")[1].GetProperty("name").GetString().Should().Be("name");
        result.RootElement.GetProperty("data").GetProperty("rows")[0][1].GetString().Should().Be("canary");
    }

    /// <summary>
    /// Rejects multiple statements before any database execution.
    /// </summary>
    [Fact]
    public async Task QueryRejectsMultipleStatements()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER)", createIfMissing: true);

        var action = () => tool.QueryAsync("data.db", "SELECT 1; SELECT 2");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Rejects SQLite transaction-control aliases before native execution.
    /// </summary>
    [Theory]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("END")]
    [InlineData("ROLLBACK")]
    public async Task QueryRejectsTransactionControlStatements(string sql)
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER)", createIfMissing: true);

        var action = () => tool.QueryAsync("data.db", sql);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Does not expose SQLite's internal schema tables through read aliases.
    /// </summary>
    [Fact]
    public async Task ReadAliasesRejectInternalSqliteSchemaObjects()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER)", createIfMissing: true);

        var query = () => tool.QueryAsync("data.db", "SELECT name FROM sqlite_master");
        var describe = () => tool.DescribeTableAsync("data.db", "sqlite_sequence");

        await query.Should().ThrowAsync<InvalidOperationException>();
        await describe.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Rejects an explicit non-positive result limit instead of silently changing it.
    /// </summary>
    [Fact]
    public async Task QueryRejectsNonPositiveRowLimit()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER)", createIfMissing: true);

        var action = () => tool.QueryAsync("data.db", "SELECT id FROM items", maxRows: 0);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Bounds schema enumeration by the complete configured result size.
    /// </summary>
    [Fact]
    public async Task ListObjectsTruncatesAtTheConfiguredResultCharacterLimit()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        var longName = new string('x', 5_000);
        await writer.ExecuteAsync("data.db", $"CREATE TABLE [{longName}] (id INTEGER)", createIfMissing: true);
        var reader = CreateTool(
            allowWrite: false,
            sqliteOptions: new SQLiteOptions { MaxResultCharacters = 4_096 });

        var result = await reader.ListObjectsAsync("data.db");

        using var document = JsonDocument.Parse(result);
        document.RootElement.GetProperty("data").GetRawText().Length.Should().BeLessThanOrEqualTo(4_096);
        document.RootElement.GetProperty("data").GetProperty("resultTruncated").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("data").GetProperty("objects").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Bounds table descriptions without splitting oversized schema metadata.
    /// </summary>
    [Fact]
    public async Task DescribeTableTruncatesAtTheConfiguredResultCharacterLimit()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        var longName = new string('y', 5_000);
        await writer.ExecuteAsync("data.db", $"CREATE TABLE [{longName}] (id INTEGER)", createIfMissing: true);
        var reader = CreateTool(
            allowWrite: false,
            sqliteOptions: new SQLiteOptions { MaxResultCharacters = 4_096 });

        var result = await reader.DescribeTableAsync("data.db", longName);

        using var document = JsonDocument.Parse(result);
        var data = document.RootElement.GetProperty("data");
        data.GetRawText().Length.Should().BeLessThanOrEqualTo(4_096);
        data.GetProperty("resultTruncated").GetBoolean().Should().BeTrue();
        data.GetProperty("columns").GetArrayLength().Should().Be(0);
        data.GetProperty("tableName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Binds only the exact native names declared by a SQLite statement.
    /// </summary>
    [Fact]
    public async Task ExecuteRequiresExactNamedParameterCatalog()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items (name TEXT)", createIfMissing: true);

        using var valid = JsonDocument.Parse("{\"@name\":\"canary\"}");
        await tool.ExecuteAsync("data.db", "INSERT INTO items VALUES (@name)", valid.RootElement);
        using var missingPrefix = JsonDocument.Parse("{\"name\":\"canary\"}");
        using var unused = JsonDocument.Parse("{\"@other\":\"canary\"}");

        var missingPrefixAction = () => tool.ExecuteAsync("data.db", "INSERT INTO items VALUES (@name)", missingPrefix.RootElement);
        var unusedAction = () => tool.ExecuteAsync("data.db", "INSERT INTO items VALUES (@name)", unused.RootElement);
        var positionalAction = () => tool.ExecuteAsync("data.db", "INSERT INTO items VALUES (?)");

        await missingPrefixAction.Should().ThrowAsync<InvalidOperationException>();
        await unusedAction.Should().ThrowAsync<InvalidOperationException>();
        await positionalAction.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Does not mistake quoted identifiers or literals for forbidden SQLite control words.
    /// </summary>
    [Fact]
    public async Task QueryIgnoresForbiddenWordsInsideQuotedValuesAndComments()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items ([attach] TEXT)", createIfMissing: true);

        var result = await tool.QueryAsync("data.db", "/* ATTACH ignored */ SELECT 'attach' AS [attach]");

        result.Should().Contain("attach");
    }

    /// <summary>
    /// Rejects modifying statements that would return database values.
    /// </summary>
    [Fact]
    public async Task ExecuteRejectsReturningBeforeCommitting()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items (name TEXT)", createIfMissing: true);

        var action = () => tool.ExecuteAsync("data.db", "INSERT INTO items VALUES ('sqlite-canary') RETURNING name");

        await action.Should().ThrowAsync<InvalidOperationException>();
        var result = await tool.QueryAsync("data.db", "SELECT name FROM items");
        result.Should().NotContain("sqlite-canary");
    }

    /// <summary>
    /// Interrupts a real long-running SQLite statement and returns caller cancellation.
    /// </summary>
    [Fact]
    public async Task QueryCancellationUsesNativeSqliteInterrupt()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER)", createIfMissing: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var action = () => tool.QueryAsync(
            "data.db",
            "WITH RECURSIVE counter(value) AS (VALUES(1) UNION ALL SELECT value + 1 FROM counter) SELECT sum(value) FROM counter",
            cancellationToken: cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Returns schema metadata without exposing raw schema SQL.
    /// </summary>
    [Fact]
    public async Task DescribeTableReturnsColumnsForeignKeysAndIndexes()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE parents (id INTEGER PRIMARY KEY)", createIfMissing: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE children (parentId INTEGER REFERENCES parents(id), name TEXT)");
        await tool.ExecuteAsync("data.db", "CREATE INDEX children_name ON children(name)");

        var result = await tool.DescribeTableAsync("data.db", "children");

        result.Should().Contain("parentId");
        result.Should().Contain("foreignKeys");
        result.Should().Contain("children_name");
        result.Should().NotContain("CREATE TABLE");
    }

    /// <summary>
    /// Truncates at a whole row boundary when a serialized result reaches its configured cap.
    /// </summary>
    [Fact]
    public async Task QueryPreservesAcceptedRowsWhenResultCharacterLimitIsReached()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER)", createIfMissing: true);
        var reader = CreateTool(
            allowWrite: false,
            sqliteOptions: new SQLiteOptions { MaxResultCharacters = 4_096 });

        var json = await reader.QueryAsync(
            "data.db",
            "SELECT printf('%.*c', 3000, 'x') UNION ALL SELECT printf('%.*c', 3000, 'x')");

        using var result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("data").GetProperty("rows").GetArrayLength().Should().Be(1);
        result.RootElement.GetProperty("data").GetProperty("resultTruncated").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Delivers BLOBs with an explicit deterministic omission marker.
    /// </summary>
    [Fact]
    public async Task QueryUsesTheStableBlobShapeAndHonorsTheBlobLimit()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("data.db", "CREATE TABLE items (payload BLOB)", createIfMissing: true);
        await writer.ExecuteAsync("data.db", "INSERT INTO items VALUES (X'0102')");
        var reader = CreateTool(allowWrite: false);
        var omittingReader = CreateTool(
            allowWrite: false,
            sqliteOptions: new SQLiteOptions { MaxBlobBytes = 0 });

        var delivered = await reader.QueryAsync("data.db", "SELECT payload FROM items");
        var omitted = await omittingReader.QueryAsync("data.db", "SELECT payload FROM items");

        using var deliveredDocument = JsonDocument.Parse(delivered);
        var deliveredBlob = deliveredDocument.RootElement.GetProperty("data").GetProperty("rows")[0][0];
        deliveredBlob.GetProperty("$type").GetString().Should().Be("blob");
        deliveredBlob.GetProperty("base64").GetString().Should().Be("AQI=");
        deliveredBlob.GetProperty("omitted").GetBoolean().Should().BeFalse();
        using var omittedDocument = JsonDocument.Parse(omitted);
        var omittedBlob = omittedDocument.RootElement.GetProperty("data").GetProperty("rows")[0][0];
        omittedBlob.GetProperty("base64").ValueKind.Should().Be(JsonValueKind.Null);
        omittedBlob.GetProperty("omitted").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Removes only the exclusively claimed new database after an uncommitted write failure.
    /// </summary>
    [Fact]
    public async Task ExecuteFailureCleansUpAnOwnedNewDatabase()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);

        var action = () => tool.ExecuteAsync("failed.db", "INSERT INTO missing_table VALUES (1)", createIfMissing: true);

        await action.Should().ThrowAsync<Exception>();
        File.Exists(Path.Combine(_root, "failed.db")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "failed.db-journal")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "failed.db-wal")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "failed.db-shm")).Should().BeFalse();
    }

    /// <summary>
    /// Rejects SQLite URI, memory, and device-style paths before opening a connection.
    /// </summary>
    [Theory]
    [InlineData(":memory:")]
    [InlineData("file:temporary.db?mode=memory")]
    [InlineData("\\\\.\\PhysicalDrive0")]
    public async Task QueryRejectsNonFileDatabaseTargets(string databasePath)
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: false);

        var action = () => tool.QueryAsync(databasePath, "SELECT 1");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Does not expose SQLite-native identifiers in an operation failure.
    /// </summary>
    [Fact]
    public async Task QuerySanitizesNativeSqliteErrors()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: true);
        await tool.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER)", createIfMissing: true);

        var action = () => tool.QueryAsync("data.db", "SELECT * FROM secret_table_canary");

        var exception = await action.Should().ThrowAsync<SqliteToolException>();
        exception.Which.Category.Should().Be(SqliteToolErrorCategory.InvalidStatement);
        exception.Which.Message.Should().Be("The SQLite statement is invalid for this alias.");
        exception.Which.ToString().Should().NotContain("secret_table_canary");
    }

    /// <summary>
    /// Does not expose a database filename through a workflow-visible SQLite failure.
    /// </summary>
    [Fact]
    public async Task QuerySanitizesPathFailures()
    {
        Directory.CreateDirectory(_root);
        var tool = CreateTool(allowWrite: false);

        var action = () => tool.QueryAsync("private-path-canary.db", "SELECT 1");

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("SQLite operation failed.");
        exception.Which.ToString().Should().NotContain("private-path-canary");
    }

    /// <summary>
    /// Withholds raw database data when requested screening is globally unavailable.
    /// </summary>
    [Fact]
    public async Task QueryWithRequestedGuardrailsFailsClosedWhenScreeningIsDisabled()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("data.db", "CREATE TABLE items (name TEXT)", createIfMissing: true);
        await writer.ExecuteAsync("data.db", "INSERT INTO items VALUES ('sqlite-canary')");
        var protectedReader = CreateTool(
            allowWrite: false,
            sqliteGuardrailsEnabled: true,
            screeningEnabled: false);

        var result = await protectedReader.QueryAsync("data.db", "SELECT name FROM items");

        result.Should().NotContain("sqlite-canary");
        using var document = JsonDocument.Parse(result);
        document.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("trustStatus").GetString().Should().Be("ScreeningUnavailable");
    }

    /// <summary>
    /// Delivers a clean screened result through the stable structured envelope.
    /// </summary>
    [Fact]
    public async Task QueryWithCleanGuardrailsReturnsStructuredDataEnvelope()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("data.db", "CREATE TABLE items (name TEXT)", createIfMissing: true);
        await writer.ExecuteAsync("data.db", "INSERT INTO items VALUES ('sqlite-canary')");
        var trust = new StubContentTrustService();
        var reader = CreateTool(
            allowWrite: false,
            sqliteGuardrailsEnabled: true,
            contentTrustService: trust);

        var result = await reader.QueryAsync("data.db", "SELECT name FROM items");

        using var document = JsonDocument.Parse(result);
        document.RootElement.GetProperty("data").GetProperty("rows")[0][0].GetString().Should().Be("sqlite-canary");
        document.RootElement.GetProperty("protectedData").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("screened").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("trustStatus").GetString().Should().Be("Clean");
        document.RootElement.GetProperty("screeningChunkCount").GetInt32().Should().Be(1);
        trust.ScreenBatches.Should().ContainSingle();
        trust.ScreenedContents.Should().BeEmpty();
        trust.SummarizedContents.Should().BeEmpty();
        trust.CompactBatches.Should().BeEmpty();
    }

    /// <summary>
    /// Fails closed before issuing an unbounded number of Guardrails requests.
    /// </summary>
    [Fact]
    public async Task QueryWithMoreThanThirtySixScreeningChunksFailsClosedWithoutScreening()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("data.db", "CREATE TABLE items (id INTEGER)", createIfMissing: true);
        var trust = new StubContentTrustService();
        var reader = CreateTool(
            allowWrite: false,
            sqliteGuardrailsEnabled: true,
            contentTrustService: trust,
            sqliteOptions: new SQLiteOptions { MaxResultCharacters = 100_000 },
            guardrailsOptions: new GuardrailsOptions { MaxContentChars = 2_048 });

        var result = await reader.QueryAsync("data.db", "SELECT printf('%.*c', 80000, 'x')");

        using var document = JsonDocument.Parse(result);
        document.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("trustStatus").GetString().Should().Be("ScreeningUnavailable");
        trust.ScreenedContents.Should().BeEmpty();
        trust.ScreenBatches.Should().BeEmpty();
    }

    /// <summary>
    /// Withholds every row when a complete read result receives a low trust score.
    /// </summary>
    [Fact]
    public async Task QueryWithLowTrustScoreWithholdsTheCompleteResult()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("data.db", "CREATE TABLE items (name TEXT)", createIfMissing: true);
        await writer.ExecuteAsync("data.db", "INSERT INTO items VALUES ('sqlite-canary')");
        var trust = new StubContentTrustService
        {
            ScreenCompactHandler = static (items, _) => Task.FromResult(
                new CompactExternalContentBatchResult(
                    items.Select(static item => new CompactExternalContentResult(
                        item.Id,
                        null,
                        new ContentTrustResult(1, item.Content, "none", ContentTrustStatus.Screened, [], "Stub", 0),
                        null,
                        CompactExternalContentStatus.SourceWithheld,
                        true,
                        0)).ToArray(),
                    [])),
        };
        var protectedReader = CreateTool(
            allowWrite: false,
            sqliteGuardrailsEnabled: true,
            contentTrustService: trust);

        var result = await protectedReader.QueryAsync("data.db", "SELECT name FROM items");

        result.Should().NotContain("sqlite-canary");
        using var document = JsonDocument.Parse(result);
        document.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("trustStatus").GetString().Should().Be("Withheld");
        trust.ScreenBatches.Should().ContainSingle();
        trust.ScreenBatches[0].Should().ContainSingle();
    }

    /// <summary>
    /// Reports one content-free incident for every failed Guardrails batch phase.
    /// </summary>
    [Fact]
    public async Task QueryReportsOneContentFreeIncidentPerGuardrailsBatchFailure()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("path-canary.db", "CREATE TABLE [schema-canary] (value TEXT)", createIfMissing: true);
        await writer.ExecuteAsync("path-canary.db", "INSERT INTO [schema-canary] VALUES ('value-canary')");
        var incidents = new StubIncidentReporter();
        var trust = new StubContentTrustService
        {
            ScreenCompactHandler = static (_, _) => Task.FromResult(
                new CompactExternalContentBatchResult(
                    [],
                    [
                        new CompactExternalContentBatchFailure("screen", new CompactExternalContentPhaseException("screen", "provider", "model", 1, 1), 1),
                        new CompactExternalContentBatchFailure("delivery", new CompactExternalContentPhaseException("delivery", "provider", "model", 1, 1), 1),
                    ])),
        };
        var reader = CreateTool(
            allowWrite: false,
            sqliteGuardrailsEnabled: true,
            contentTrustService: trust,
            incidentReporter: incidents);

        var result = await reader.QueryAsync("path-canary.db", "SELECT value FROM [schema-canary]");

        result.Should().NotContain("value-canary");
        incidents.Reports.Should().HaveCount(2);
        incidents.Reports.Should().OnlyContain(report => report.Source == "SqliteTool.Guardrails");
        incidents.Reports.Select(report => report.Exception.ToString())
            .Should().NotContainMatch("*path-canary*", "*schema-canary*", "*value-canary*", "*SELECT value*");
    }

    /// <summary>
    /// Converts an unexpected screening exception into one content-free incident without leaking SQLite canaries to logs.
    /// </summary>
    [Fact]
    public async Task QuerySanitizesUnexpectedScreeningFailureInIncidentsAndLogs()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("path-canary.db", "CREATE TABLE [schema-canary] (value TEXT)", createIfMissing: true);
        await writer.ExecuteAsync("path-canary.db", "INSERT INTO [schema-canary] VALUES ('value-canary')");
        var incidents = new StubIncidentReporter();
        var logger = new CapturingLogger();
        var trust = new StubContentTrustService
        {
            ScreenCompactHandler = static (_, _) => throw new InvalidOperationException("value-canary schema-canary path-canary SELECT value"),
        };
        var reader = CreateTool(
            allowWrite: false,
            sqliteGuardrailsEnabled: true,
            contentTrustService: trust,
            incidentReporter: incidents,
            logger: logger);

        var result = await reader.QueryAsync("path-canary.db", "SELECT value FROM [schema-canary]");

        result.Should().NotContain("value-canary");
        incidents.Reports.Should().ContainSingle();
        incidents.Reports[0].Exception.Message.Should().Be("SQLite Guardrails screening failed unexpectedly.");
        logger.Messages.Should().NotContainMatch("*path-canary*", "*schema-canary*", "*value-canary*", "*SELECT value*");
    }

    /// <summary>
    /// Does not report an incident when the caller cancels a screening operation.
    /// </summary>
    [Fact]
    public async Task QueryCancellationDuringScreeningDoesNotReportAnIncident()
    {
        Directory.CreateDirectory(_root);
        var writer = CreateTool(allowWrite: true);
        await writer.ExecuteAsync("data.db", "CREATE TABLE items (value TEXT)", createIfMissing: true);
        await writer.ExecuteAsync("data.db", "INSERT INTO items VALUES ('value')");
        using var cancellationSource = new CancellationTokenSource();
        var incidents = new StubIncidentReporter();
        var trust = new StubContentTrustService
        {
            ScreenCompactHandler = (_, _) =>
            {
                cancellationSource.Cancel();
                return Task.FromCanceled<CompactExternalContentBatchResult>(cancellationSource.Token);
            },
        };
        var reader = CreateTool(
            allowWrite: false,
            sqliteGuardrailsEnabled: true,
            contentTrustService: trust,
            incidentReporter: incidents);

        var action = () => reader.QueryAsync("data.db", "SELECT value FROM items", cancellationToken: cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        incidents.Reports.Should().BeEmpty();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private SqliteTool CreateTool(
        bool allowWrite,
        bool sqliteGuardrailsEnabled = false,
        bool screeningEnabled = true,
        StubContentTrustService? contentTrustService = null,
        SQLiteOptions? sqliteOptions = null,
        GuardrailsOptions? guardrailsOptions = null,
        StubIncidentReporter? incidentReporter = null,
        ILogger<SqliteTool>? logger = null)
    {
        var fileSystem = Options.Create(new FileSystemOptions { RootPath = _root });
        return new SqliteTool(
            new PathSandbox(fileSystem),
            Options.Create(sqliteOptions ?? new SQLiteOptions()),
            new MutableOptionsMonitor<GuardrailsOptions>(guardrailsOptions ?? new GuardrailsOptions
            {
                ScreeningEnabled = screeningEnabled,
            }),
            contentTrustService ?? new StubContentTrustService(),
            new ToolExecutionContext(null, [], sqliteGuardrailsEnabled),
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SqliteTool>.Instance,
            TimeProvider.System,
            incidentReporter ?? new StubIncidentReporter());
    }

    private sealed class CapturingLogger : ILogger<SqliteTool>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
