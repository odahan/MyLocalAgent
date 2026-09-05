using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>Implements the task-owned editorial proposal-memory contract.</summary>
public sealed partial class ProposalMemoryService : IProposalMemoryService
{
    private const string TableName = "editorial_proposals";
    private const string CreateTableSql = """
        CREATE TABLE editorial_proposals (
          id INTEGER PRIMARY KEY,
          title TEXT NOT NULL,
          summary TEXT NOT NULL,
          source_url TEXT NULL,
          created_at_utc TEXT NOT NULL,
          used INTEGER NOT NULL DEFAULT 0 CHECK (used IN (0, 1)),
          proposal_key TEXT NOT NULL UNIQUE,
          embedding_model TEXT NOT NULL,
          embedding_dimension INTEGER NOT NULL,
          embedding BLOB NOT NULL
        )
        """;

    private static readonly IReadOnlyDictionary<string, string> RequiredColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "INTEGER",
            ["title"] = "TEXT",
            ["summary"] = "TEXT",
            ["source_url"] = "TEXT",
            ["created_at_utc"] = "TEXT",
            ["used"] = "INTEGER",
            ["proposal_key"] = "TEXT",
            ["embedding_model"] = "TEXT",
            ["embedding_dimension"] = "INTEGER",
            ["embedding"] = "BLOB",
        };

    private readonly IKnowledgeEmbeddingService _embeddingService;
    private readonly IOptionsMonitor<KnowledgeOptions> _knowledgeOptions;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the proposal-memory service.</summary>
    public ProposalMemoryService(
        IKnowledgeEmbeddingService embeddingService,
        IOptionsMonitor<KnowledgeOptions> knowledgeOptions,
        TimeProvider timeProvider)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _knowledgeOptions = knowledgeOptions ?? throw new ArgumentNullException(nameof(knowledgeOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        SqliteNativeBootstrap.Initialize();
    }

    /// <inheritdoc />
    public async Task<ProposalDatabaseResult> CreateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existed = File.Exists(databasePath);
        if (!existed && !Directory.Exists(Path.GetDirectoryName(databasePath)))
        {
            throw new DirectoryNotFoundException("The authorized proposal database parent directory does not exist.");
        }

        try
        {
            using var connection = Open(databasePath, SqliteOpenMode.ReadWriteCreate);
            using var interrupt = RegisterInterrupt(connection, cancellationToken);
            var tableExists = TableExists(connection, null);
            IReadOnlyList<LegacyProposal> legacyRows = [];
            IReadOnlyList<float[]> legacyVectors = [];
            string? migrationModel = null;
            var requiresMigration = false;
            if (tableExists)
            {
                var columns = ReadColumns(connection, null);
                requiresMigration = RequiredColumns.Keys.Any(column => !columns.ContainsKey(column));
                if (requiresMigration)
                {
                    var migrationColumns = new[] { "proposal_key", "embedding_model", "embedding_dimension", "embedding" };
                    if (migrationColumns.Any(columns.ContainsKey))
                    {
                        throw new InvalidOperationException("The proposal-memory database schema is incompatible.");
                    }

                    ValidateLegacySchema(columns);
                    legacyRows = ReadLegacyRows(connection);
                    migrationModel = GetEmbeddingModel();
                    if (legacyRows.Count > 0)
                    {
                        legacyVectors = await _embeddingService.GenerateAsync(
                            legacyRows.Select(static row => EditorialProjection(row.Title, row.Summary)).ToArray(),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            using var transaction = connection.BeginTransaction();
            if (!tableExists)
            {
                using var create = connection.CreateCommand();
                create.Transaction = transaction;
                create.CommandText = CreateTableSql;
                create.ExecuteNonQuery();
            }
            else if (requiresMigration)
            {
                MigrateLegacySchema(connection, transaction, legacyRows, legacyVectors, migrationModel!);
                ValidateSchema(connection, transaction);
            }
            else
            {
                ValidateSchema(connection, transaction);
            }

            EnsureEditorialSchema(connection, transaction);

            transaction.Commit();
            return new ProposalDatabaseResult(!existed, !tableExists, true);
        }
        catch
        {
            if (!existed)
            {
                DeleteIfPresent(databasePath);
                DeleteIfPresent(databasePath + "-journal");
                DeleteIfPresent(databasePath + "-wal");
                DeleteIfPresent(databasePath + "-shm");
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ProposalStoreResult> StoreAsync(
        string databasePath,
        string title,
        string summary,
        string? sourceUrl,
        bool used,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        var normalizedTitle = NormalizeTitle(title);
        if (normalizedTitle.Length == 0)
        {
            throw new ArgumentException("The proposal title must contain letters or digits.", nameof(title));
        }

        var canonicalUrl = NormalizeUrl(sourceUrl);
        var projection = EditorialProjection(title, summary);
        var model = GetEmbeddingModel();
        var vectors = await _embeddingService.GenerateAsync([projection], cancellationToken).ConfigureAwait(false);
        var vector = vectors.Single();
        var key = CreateProposalKey(normalizedTitle, canonicalUrl);

        return await Task.Run(() =>
        {
            using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
            using var interrupt = RegisterInterrupt(connection, cancellationToken);
            using var transaction = connection.BeginTransaction();
            ValidateSchema(connection, transaction);
            ValidateRowValues(connection, transaction);
            var existed = ProposalExists(connection, transaction, key);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO editorial_proposals
                    (title, summary, source_url, created_at_utc, used, proposal_key,
                     embedding_model, embedding_dimension, embedding)
                VALUES
                    ($title, $summary, $sourceUrl, $createdAtUtc, $used, $proposalKey,
                     $embeddingModel, $embeddingDimension, $embedding)
                ON CONFLICT(proposal_key) DO UPDATE SET
                    title = excluded.title,
                    summary = excluded.summary,
                    source_url = excluded.source_url,
                    used = MAX(editorial_proposals.used, excluded.used),
                    embedding_model = excluded.embedding_model,
                    embedding_dimension = excluded.embedding_dimension,
                    embedding = excluded.embedding
                """;
            command.Parameters.AddWithValue("$title", title.Trim());
            command.Parameters.AddWithValue("$summary", summary.Trim());
            command.Parameters.AddWithValue("$sourceUrl", (object?)canonicalUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAtUtc", _timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$used", used ? 1 : 0);
            command.Parameters.AddWithValue("$proposalKey", key);
            command.Parameters.AddWithValue("$embeddingModel", model);
            command.Parameters.AddWithValue("$embeddingDimension", vector.Length);
            command.Parameters.Add("$embedding", SqliteType.Blob).Value = EncodeVector(vector);
            command.ExecuteNonQuery();
            transaction.Commit();
            return new ProposalStoreResult(!existed, key, model, vector.Length);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProposalFilterResult> FilterAsync(
        string databasePath,
        IReadOnlyList<WebDocument> candidates,
        int lookbackMonths,
        bool useUsedTag,
        double semanticSimilarityThreshold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (lookbackMonths is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(lookbackMonths));
        }

        if (semanticSimilarityThreshold is < 0.80 or > 0.99 || !double.IsFinite(semanticSimilarityThreshold))
        {
            throw new ArgumentOutOfRangeException(nameof(semanticSimilarityThreshold));
        }

        var cutoff = _timeProvider.GetUtcNow().AddMonths(-lookbackMonths);
        var proposals = await ReadApplicableAsync(databasePath, cutoff, useUsedTag, cancellationToken).ConfigureAwait(false);
        var retained = new List<WebDocument>(candidates.Count);
        var semanticCandidates = new List<WebDocument>(candidates.Count);
        var proposalTitles = proposals.Select(static item => item.NormalizedTitle).ToHashSet(StringComparer.Ordinal);
        var proposalUrls = proposals.Where(static item => item.CanonicalUrl is not null)
            .Select(static item => item.CanonicalUrl!)
            .ToHashSet(StringComparer.Ordinal);
        var exactCount = 0;
        foreach (var candidate in candidates)
        {
            var titleMatch = proposalTitles.Contains(NormalizeTitle(candidate.Title));
            var candidateUrl = NormalizeUrl(candidate.CanonicalUrl);
            var urlMatch = candidateUrl is not null && proposalUrls.Contains(candidateUrl);
            if (titleMatch || urlMatch)
            {
                exactCount++;
            }
            else
            {
                semanticCandidates.Add(candidate);
            }
        }

        if (semanticCandidates.Count == 0)
        {
            return new ProposalFilterResult([], proposals.Count, 0, exactCount, 0);
        }

        var model = GetEmbeddingModel();
        var candidateVectors = await _embeddingService.GenerateAsync(
                semanticCandidates.Select(static candidate => EditorialProjection(candidate.Title, candidate.ShortSummary)).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var dimension = candidateVectors[0].Length;
        var compatible = proposals.Where(item =>
            string.Equals(item.EmbeddingModel, model, StringComparison.Ordinal) &&
            item.EmbeddingDimension == dimension &&
            item.Embedding.Length == dimension).ToArray();
        var incompatibleCount = proposals.Count - compatible.Length;
        var semanticCount = 0;
        for (var index = 0; index < semanticCandidates.Count; index++)
        {
            var matches = compatible.Any(proposal =>
                Cosine(candidateVectors[index], proposal.Embedding) >= semanticSimilarityThreshold);
            if (matches)
            {
                semanticCount++;
            }
            else
            {
                retained.Add(semanticCandidates[index]);
            }
        }

        return new ProposalFilterResult(retained, proposals.Count, incompatibleCount, exactCount, semanticCount);
    }

    internal static string NormalizeTitle(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(rune.ToString());
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    internal static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var canonical = UrlCanonicalizer.Canonicalize(value.Trim());
        return canonical.Length == 0 ? null : canonical;
    }

    internal static string EditorialProjection(string title, string summary) =>
        $"{title.Trim()}\n{summary.Trim()}";

    private async Task<IReadOnlyList<StoredProposal>> ReadApplicableAsync(
        string databasePath,
        DateTimeOffset cutoff,
        bool useUsedTag,
        CancellationToken cancellationToken) => await Task.Run(() =>
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        using var interrupt = RegisterInterrupt(connection, cancellationToken);
        ValidateSchema(connection, null);
        ValidateRowValues(connection, null);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT title, source_url, created_at_utc, used,
                   embedding_model, embedding_dimension, embedding
            FROM editorial_proposals
            WHERE julianday(created_at_utc) >= julianday($cutoff) OR ($useUsedTag = 1 AND used = 1)
            ORDER BY id
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$useUsedTag", useUsedTag ? 1 : 0);
        using var reader = command.ExecuteReader();
        var output = new List<StoredProposal>();
        while (reader.Read())
        {
            if (!DateTimeOffset.TryParse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _) ||
                reader.GetInt64(3) is not (0 or 1) ||
                reader.GetInt64(5) is <= 0 or > int.MaxValue)
            {
                throw new InvalidOperationException("The proposal-memory database contains an incompatible row.");
            }

            var dimension = reader.GetInt32(5);
            var bytes = (byte[])reader.GetValue(6);
            output.Add(new StoredProposal(
                NormalizeTitle(reader.GetString(0)),
                reader.IsDBNull(1) ? null : NormalizeUrl(reader.GetString(1)),
                reader.GetString(4),
                dimension,
                DecodeVector(bytes, dimension)));
        }

        return output;
    }, cancellationToken).ConfigureAwait(false);

    private string GetEmbeddingModel()
    {
        var options = _knowledgeOptions.CurrentValue;
        if (!string.Equals(options.EmbeddingProvider?.Trim(), "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Proposal memory requires the local Ollama embedding provider.");
        }

        var model = options.EmbeddingModel?.Trim();
        return string.IsNullOrEmpty(model)
            ? throw new InvalidOperationException("The local embedding model is unavailable.")
            : model;
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        if (!File.Exists(path) && mode == SqliteOpenMode.ReadOnly)
        {
            throw new FileNotFoundException("The proposal-memory database file does not exist.");
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static bool TableExists(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", TableName);
        return command.ExecuteScalar() is not null;
    }

    private static void ValidateSchema(SqliteConnection connection, SqliteTransaction? transaction)
    {
        if (!TableExists(connection, transaction))
        {
            throw new InvalidOperationException("The proposal-memory database has no compatible editorial_proposals table.");
        }

        var columns = ReadColumns(connection, transaction);

        foreach (var required in RequiredColumns)
        {
            if (!columns.TryGetValue(required.Key, out var declaredType) ||
                !string.Equals(declaredType.Trim(), required.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The proposal-memory database schema is incompatible.");
            }
        }
    }

    private static void ValidateRowValues(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT count(*)
            FROM editorial_proposals
            WHERE typeof(title) <> 'text'
               OR typeof(summary) <> 'text'
               OR (source_url IS NOT NULL AND typeof(source_url) <> 'text')
               OR typeof(created_at_utc) <> 'text'
               OR julianday(created_at_utc) IS NULL
               OR typeof(used) <> 'integer'
               OR used NOT IN (0, 1)
               OR typeof(proposal_key) <> 'text'
               OR length(proposal_key) = 0
               OR typeof(embedding_model) <> 'text'
               OR length(embedding_model) = 0
               OR typeof(embedding_dimension) <> 'integer'
               OR embedding_dimension <= 0
               OR typeof(embedding) <> 'blob'
               OR length(embedding) <> embedding_dimension * 4
            """;
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0)
        {
            throw new InvalidOperationException("The proposal-memory database contains an incompatible row.");
        }
    }

    private static Dictionary<string, string> ReadColumns(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA table_info(editorial_proposals)";
        using var reader = command.ExecuteReader();
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns[reader.GetString(1)] = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        }

        return columns;
    }

    private static void ValidateLegacySchema(IReadOnlyDictionary<string, string> columns)
    {
        foreach (var required in RequiredColumns.Where(static pair =>
                     pair.Key is "id" or "title" or "summary" or "source_url" or "created_at_utc" or "used"))
        {
            if (!columns.TryGetValue(required.Key, out var declaredType) ||
                !string.Equals(declaredType.Trim(), required.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The proposal-memory database schema is incompatible.");
            }
        }
    }

    private static IReadOnlyList<LegacyProposal> ReadLegacyRows(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, summary, source_url, created_at_utc, used FROM editorial_proposals ORDER BY id";
        using var reader = command.ExecuteReader();
        var rows = new List<LegacyProposal>();
        while (reader.Read())
        {
            var used = reader.GetInt64(5);
            if (used is not (0 or 1) ||
                !DateTimeOffset.TryParse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            {
                throw new InvalidOperationException("The proposal-memory database contains an incompatible row.");
            }

            rows.Add(new LegacyProposal(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                used != 0));
        }

        return rows;
    }

    private static void MigrateLegacySchema(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<LegacyProposal> rows,
        IReadOnlyList<float[]> vectors,
        string model)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = CreateTableSql.Replace(
                "editorial_proposals",
                "editorial_proposals_migration",
                StringComparison.Ordinal);
            command.ExecuteNonQuery();
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var vector = vectors[index];
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO editorial_proposals_migration
                    (id, title, summary, source_url, created_at_utc, used, proposal_key,
                     embedding_model, embedding_dimension, embedding)
                VALUES
                    ($id, $title, $summary, $sourceUrl, $createdAtUtc, $used, $key,
                     $model, $dimension, $embedding)
                """;
            command.Parameters.AddWithValue("$id", row.Id);
            command.Parameters.AddWithValue("$title", row.Title);
            command.Parameters.AddWithValue("$summary", row.Summary);
            command.Parameters.AddWithValue("$sourceUrl", (object?)NormalizeUrl(row.SourceUrl) ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAtUtc", row.CreatedAtUtc);
            command.Parameters.AddWithValue("$used", row.Used ? 1 : 0);
            command.Parameters.AddWithValue("$key", CreateProposalKey(NormalizeTitle(row.Title), NormalizeUrl(row.SourceUrl)));
            command.Parameters.AddWithValue("$model", model);
            command.Parameters.AddWithValue("$dimension", vector.Length);
            command.Parameters.Add("$embedding", SqliteType.Blob).Value = EncodeVector(vector);
            command.ExecuteNonQuery();
        }

        using (var drop = connection.CreateCommand())
        {
            drop.Transaction = transaction;
            drop.CommandText = "DROP TABLE editorial_proposals";
            drop.ExecuteNonQuery();
        }

        using var rename = connection.CreateCommand();
        rename.Transaction = transaction;
        rename.CommandText = "ALTER TABLE editorial_proposals_migration RENAME TO editorial_proposals";
        rename.ExecuteNonQuery();
    }

    private static bool ProposalExists(SqliteConnection connection, SqliteTransaction transaction, string key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM editorial_proposals WHERE proposal_key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is not null;
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string CreateProposalKey(string normalizedTitle, string? canonicalUrl)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{normalizedTitle}\u001f{canonicalUrl ?? string.Empty}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] EncodeVector(IReadOnlyList<float> vector)
    {
        var output = new byte[vector.Count * sizeof(float)];
        for (var index = 0; index < vector.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(index * sizeof(float), sizeof(float)), BitConverter.SingleToInt32Bits(vector[index]));
        }

        return output;
    }

    private static float[] DecodeVector(byte[] bytes, int dimension)
    {
        if (bytes.Length != dimension * sizeof(float))
        {
            return [];
        }

        var output = new float[dimension];
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float))));
        }

        return output.Any(static value => !float.IsFinite(value)) ? [] : output;
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        return leftMagnitude <= 0 || rightMagnitude <= 0
            ? double.NegativeInfinity
            : dot / Math.Sqrt(leftMagnitude * rightMagnitude);
    }

    private static CancellationTokenRegistration RegisterInterrupt(SqliteConnection connection, CancellationToken cancellationToken) =>
        cancellationToken.Register(static state => SQLitePCL.raw.sqlite3_interrupt(((SqliteConnection)state!).Handle), connection);

    private sealed record StoredProposal(
        string NormalizedTitle,
        string? CanonicalUrl,
        string EmbeddingModel,
        int EmbeddingDimension,
        float[] Embedding);

    private sealed record LegacyProposal(
        long Id,
        string Title,
        string Summary,
        string? SourceUrl,
        string CreatedAtUtc,
        bool Used);
}
