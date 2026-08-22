using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace OD.MyLocalAgent.Core;

internal sealed class SqliteKnowledgeStore
{
    private const int CurrentSchemaVersion = 4;
    private readonly string _databasePath;
    private readonly ILogger<SqliteKnowledgeStore>? _logger;

    public SqliteKnowledgeStore(WorkspacePaths paths, ILogger<SqliteKnowledgeStore>? logger = null)
    {
        _databasePath = paths.KnowledgeDatabasePath;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        SqliteNativeBootstrap.Initialize();
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var version = await ScalarLongAsync(connection, "PRAGMA user_version;", cancellationToken).ConfigureAwait(false);
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Knowledge database schema {version} is newer than supported schema {CurrentSchemaVersion}.");
        }

        if (version == 0)
        {
            await ExecuteAsync(connection, """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE documents(
                    id TEXT PRIMARY KEY, name TEXT NOT NULL, description TEXT NOT NULL,
                    original_file_name TEXT NOT NULL, media_type TEXT NOT NULL, size_bytes INTEGER NOT NULL,
                    character_count INTEGER NOT NULL, chunk_count INTEGER NOT NULL,
                    canonical_file_name TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Ready',
                    active_revision_id TEXT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
                CREATE TABLE document_tags(
                    document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                    normalized_tag TEXT NOT NULL,
                    PRIMARY KEY(document_id, normalized_tag));
                CREATE INDEX ix_document_tags_tag ON document_tags(normalized_tag, document_id);
                CREATE TABLE document_revisions(
                    id TEXT PRIMARY KEY,
                    document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                    original_file_name TEXT NOT NULL, canonical_file_name TEXT NOT NULL,
                    media_type TEXT NOT NULL, size_bytes INTEGER NOT NULL,
                    source_sha256 TEXT NOT NULL, extracted_text_sha256 TEXT NOT NULL,
                    embedding_provider TEXT NOT NULL, embedding_model TEXT NOT NULL,
                    dimension INTEGER NOT NULL, chunking_version INTEGER NOT NULL,
                    index_version INTEGER NOT NULL, character_count INTEGER NOT NULL,
                    chunk_count INTEGER NOT NULL, state TEXT NOT NULL,
                    cleanup_pending INTEGER NOT NULL DEFAULT 0, created_utc TEXT NOT NULL);
                CREATE INDEX ix_document_revisions_document ON document_revisions(document_id,state,created_utc);
                CREATE TABLE chunks(
                    id TEXT PRIMARY KEY, document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                    revision_id TEXT NOT NULL REFERENCES document_revisions(id) ON DELETE CASCADE,
                    ordinal INTEGER NOT NULL, text TEXT NOT NULL, estimated_tokens INTEGER NOT NULL,
                    start_character INTEGER NOT NULL, end_character INTEGER NOT NULL,
                    heading TEXT NULL, page_number INTEGER NULL,
                    UNIQUE(revision_id, ordinal));
                CREATE INDEX ix_chunks_revision_ordinal ON chunks(revision_id,ordinal);
                CREATE TABLE vectors(
                    chunk_id TEXT PRIMARY KEY REFERENCES chunks(id) ON DELETE CASCADE,
                    dimension INTEGER NOT NULL, vector BLOB NOT NULL);
                CREATE TABLE pending_file_cleanup(
                    canonical_file_name TEXT PRIMARY KEY,
                    created_utc TEXT NOT NULL);
                CREATE VIRTUAL TABLE chunk_fts USING fts5(
                    chunk_id UNINDEXED, revision_id UNINDEXED, document_id UNINDEXED,
                    heading, text, tokenize='unicode61 remove_diacritics 2');
                PRAGMA user_version=4;
                """, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 1)
        {
            await MigrateVersionOneAsync(connection, cancellationToken).ConfigureAwait(false);
            await MigrateVersionTwoAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        else if (version == 2)
        {
            await MigrateVersionTwoAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        if (version <= 3)
        {
            await MigrateVersionThreeAsync(connection, cancellationToken).ConfigureAwait(false);
        }

        await EnsureFtsConsistencyAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static Task MigrateVersionTwoAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken) =>
        ExecuteAsync(connection, """
            BEGIN IMMEDIATE;
            CREATE TABLE IF NOT EXISTS pending_file_cleanup(
                canonical_file_name TEXT PRIMARY KEY,
                created_utc TEXT NOT NULL);
            PRAGMA user_version=3;
            COMMIT;
            """, cancellationToken);

    private static async Task MigrateVersionThreeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasChunks = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='chunks';", cancellationToken).ConfigureAwait(false) == 1;
        await ExecuteAsync(connection, hasChunks ? """
            BEGIN IMMEDIATE;
            CREATE VIRTUAL TABLE IF NOT EXISTS chunk_fts USING fts5(
                chunk_id UNINDEXED, revision_id UNINDEXED, document_id UNINDEXED,
                heading, text, tokenize='unicode61 remove_diacritics 2');
            DELETE FROM chunk_fts;
            INSERT INTO chunk_fts(chunk_id,revision_id,document_id,heading,text)
            SELECT c.id,c.revision_id,c.document_id,COALESCE(c.heading,''),c.text
            FROM chunks c JOIN documents d ON d.id=c.document_id AND d.active_revision_id=c.revision_id;
            PRAGMA user_version=4;
            COMMIT;
            """ : """
            BEGIN IMMEDIATE;
            CREATE VIRTUAL TABLE IF NOT EXISTS chunk_fts USING fts5(
                chunk_id UNINDEXED, revision_id UNINDEXED, document_id UNINDEXED,
                heading, text, tokenize='unicode61 remove_diacritics 2');
            PRAGMA user_version=4;
            COMMIT;
            """, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rebuilds the application-owned FTS table only when startup detects an interrupted legacy migration.</summary>
    private async Task EnsureFtsConsistencyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var hasCoreSchema = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('documents','chunks');", cancellationToken).ConfigureAwait(false) == 2;
        if (!hasCoreSchema)
        {
            return;
        }

        var hasFts = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='chunk_fts';", cancellationToken).ConfigureAwait(false) == 1;
        var activeCount = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM chunks c JOIN documents d ON d.id=c.document_id AND d.active_revision_id=c.revision_id;", cancellationToken).ConfigureAwait(false);
        long indexedCount;
        long staleCount;
        try
        {
            indexedCount = hasFts ? await ScalarLongAsync(connection, "SELECT COUNT(*) FROM chunk_fts;", cancellationToken).ConfigureAwait(false) : -1;
            staleCount = hasFts ? await ScalarLongAsync(connection, "SELECT COUNT(*) FROM chunk_fts f LEFT JOIN chunks c ON c.id=f.chunk_id LEFT JOIN documents d ON d.id=f.document_id WHERE c.id IS NULL OR c.revision_id<>d.active_revision_id OR f.revision_id<>d.active_revision_id;", cancellationToken).ConfigureAwait(false) : 1;
        }
        catch (SqliteException)
        {
            await RebuildFtsAsync(connection, cancellationToken).ConfigureAwait(false);
            _logger?.LogWarning("Rebuilt the Knowledge FTS index after an unreadable consistency check.");
            return;
        }
        if (hasFts && activeCount == indexedCount && staleCount == 0)
        {
            return;
        }

        await RebuildFtsAsync(connection, cancellationToken).ConfigureAwait(false);
        _logger?.LogWarning("Rebuilt the Knowledge FTS index after a consistency mismatch: active chunks {ActiveChunkCount}, indexed chunks {IndexedChunkCount}, stale rows {StaleRowCount}.", activeCount, indexedCount, staleCount);
    }

    private static Task RebuildFtsAsync(SqliteConnection connection, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, """
            BEGIN IMMEDIATE;
            DROP TABLE IF EXISTS chunk_fts;
            CREATE VIRTUAL TABLE chunk_fts USING fts5(
                chunk_id UNINDEXED, revision_id UNINDEXED, document_id UNINDEXED,
                heading, text, tokenize='unicode61 remove_diacritics 2');
            INSERT INTO chunk_fts(chunk_id,revision_id,document_id,heading,text)
            SELECT c.id,c.revision_id,c.document_id,COALESCE(c.heading,''),c.text
            FROM chunks c JOIN documents d ON d.id=c.document_id AND d.active_revision_id=c.revision_id;
            COMMIT;
            """, cancellationToken);

    private static async Task MigrateVersionOneAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "PRAGMA foreign_keys=OFF;", cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, """
                BEGIN IMMEDIATE;
                ALTER TABLE vectors RENAME TO vectors_v1;
                ALTER TABLE chunks RENAME TO chunks_v1;
                ALTER TABLE document_tags RENAME TO document_tags_v1;
                ALTER TABLE documents RENAME TO documents_v1;
                CREATE TABLE documents(
                    id TEXT PRIMARY KEY,name TEXT NOT NULL,description TEXT NOT NULL,
                    original_file_name TEXT NOT NULL,media_type TEXT NOT NULL,size_bytes INTEGER NOT NULL,
                    character_count INTEGER NOT NULL,chunk_count INTEGER NOT NULL,canonical_file_name TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'Ready',active_revision_id TEXT NULL,created_utc TEXT NOT NULL,updated_utc TEXT NOT NULL);
                CREATE TABLE document_tags(
                    document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                    normalized_tag TEXT NOT NULL,PRIMARY KEY(document_id,normalized_tag));
                CREATE INDEX ix_document_tags_tag ON document_tags(normalized_tag,document_id);
                CREATE TABLE document_revisions(
                    id TEXT PRIMARY KEY,document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                    original_file_name TEXT NOT NULL,canonical_file_name TEXT NOT NULL,media_type TEXT NOT NULL,size_bytes INTEGER NOT NULL,
                    source_sha256 TEXT NOT NULL,extracted_text_sha256 TEXT NOT NULL,embedding_provider TEXT NOT NULL,
                    embedding_model TEXT NOT NULL,dimension INTEGER NOT NULL,chunking_version INTEGER NOT NULL,index_version INTEGER NOT NULL,
                    character_count INTEGER NOT NULL,chunk_count INTEGER NOT NULL,state TEXT NOT NULL,
                    cleanup_pending INTEGER NOT NULL DEFAULT 0,created_utc TEXT NOT NULL);
                CREATE INDEX ix_document_revisions_document ON document_revisions(document_id,state,created_utc);
                CREATE TABLE chunks(
                    id TEXT PRIMARY KEY,document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                    revision_id TEXT NOT NULL REFERENCES document_revisions(id) ON DELETE CASCADE,
                    ordinal INTEGER NOT NULL,text TEXT NOT NULL,estimated_tokens INTEGER NOT NULL,
                    start_character INTEGER NOT NULL,end_character INTEGER NOT NULL,heading TEXT NULL,page_number INTEGER NULL,
                    UNIQUE(revision_id,ordinal));
                CREATE INDEX ix_chunks_revision_ordinal ON chunks(revision_id,ordinal);
                CREATE TABLE vectors(
                    chunk_id TEXT PRIMARY KEY REFERENCES chunks(id) ON DELETE CASCADE,
                    dimension INTEGER NOT NULL,vector BLOB NOT NULL);
                INSERT INTO documents
                    SELECT id,name,description,original_file_name,media_type,size_bytes,character_count,chunk_count,
                           canonical_file_name,'Ready',id||':v1',updated_utc,updated_utc FROM documents_v1;
                INSERT INTO document_tags SELECT document_id,normalized_tag FROM document_tags_v1;
                INSERT INTO document_revisions
                    SELECT d.id||':v1',d.id,d.original_file_name,d.canonical_file_name,d.media_type,d.size_bytes,'','',
                           COALESCE((SELECT value FROM metadata WHERE key='embedding_provider'),''),
                           COALESCE((SELECT value FROM metadata WHERE key='embedding_model'),''),
                           COALESCE((SELECT CAST(value AS INTEGER) FROM metadata WHERE key='dimension'),0),1,1,
                           d.character_count,d.chunk_count,'Ready',0,d.updated_utc FROM documents_v1 d;
                INSERT INTO chunks
                    SELECT id,document_id,document_id||':v1',ordinal,text,estimated_tokens,start_character,end_character,heading,page_number FROM chunks_v1;
                INSERT INTO vectors SELECT chunk_id,dimension,vector FROM vectors_v1;
                DROP TABLE vectors_v1;
                DROP TABLE chunks_v1;
                DROP TABLE document_tags_v1;
                DROP TABLE documents_v1;
                PRAGMA user_version=2;
                COMMIT;
                """, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await ExecuteAsync(connection, "ROLLBACK;", CancellationToken.None).ConfigureAwait(false);
            }
            catch (SqliteException)
            {
            }
            throw;
        }
        finally
        {
            await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task CommitAsync(
        KnowledgeDocument document,
        string canonicalFileName,
        IReadOnlyList<KnowledgeChunk> chunks,
        IReadOnlyList<float[]> vectors,
        string provider,
        string model,
        CancellationToken cancellationToken)
        => await CommitAsync(
            document,
            canonicalFileName,
            chunks,
            vectors,
            new KnowledgeOptions { EmbeddingProvider = provider, EmbeddingModel = model },
            cancellationToken).ConfigureAwait(false);

    public async Task CommitAsync(
        KnowledgeDocument document,
        string canonicalFileName,
        IReadOnlyList<KnowledgeChunk> chunks,
        IReadOnlyList<float[]> vectors,
        KnowledgeOptions options,
        CancellationToken cancellationToken,
        string? revisionId = null,
        string sourceSha256 = "",
        string extractedTextSha256 = "")
    {
        if (chunks.Count != vectors.Count || chunks.Count == 0)
        {
            throw new ArgumentException("Every Knowledge chunk requires exactly one vector.", nameof(vectors));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            revisionId ??= Guid.NewGuid().ToString("N");
            await SetContractAsync(connection, transaction, options, vectors[0].Length, false).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, """
                INSERT INTO documents(id,name,description,original_file_name,media_type,size_bytes,character_count,chunk_count,canonical_file_name,status,active_revision_id,created_utc,updated_utc)
                VALUES($id,$name,$description,$file,$media,$size,$characters,$chunks,$canonical,'Ready',NULL,$updated,$updated);
                """, [
                    ("$id", document.Id), ("$name", document.Name), ("$description", document.Description),
                    ("$file", document.OriginalFileName), ("$media", document.MediaType), ("$size", document.SizeBytes),
                    ("$characters", document.CharacterCount), ("$chunks", document.ChunkCount),
                    ("$canonical", canonicalFileName), ("$updated", document.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture))
                ], CancellationToken.None).ConfigureAwait(false);
            await InsertRevisionAsync(connection, transaction, revisionId, document, canonicalFileName, options, vectors[0].Length, CancellationToken.None, sourceSha256, extractedTextSha256).ConfigureAwait(false);
            foreach (var tag in document.Tags)
            {
                await ExecuteNonQueryAsync(connection, transaction,
                    "INSERT INTO document_tags(document_id,normalized_tag) VALUES($id,$tag);",
                    [("$id", document.Id), ("$tag", tag)], CancellationToken.None).ConfigureAwait(false);
            }

            for (var index = 0; index < chunks.Count; index++)
            {
                var chunk = chunks[index];
                await ExecuteNonQueryAsync(connection, transaction, """
                    INSERT INTO chunks(id,document_id,revision_id,ordinal,text,estimated_tokens,start_character,end_character,heading,page_number)
                    VALUES($id,$document,$revision,$ordinal,$text,$tokens,$start,$end,$heading,$page);
                    """, [
                        ("$id", chunk.Id), ("$document", document.Id), ("$revision",revisionId), ("$ordinal", chunk.Ordinal), ("$text", chunk.Text),
                        ("$tokens", chunk.EstimatedTokens), ("$start", chunk.StartCharacter), ("$end", chunk.EndCharacter),
                        ("$heading", chunk.Heading), ("$page", chunk.PageNumber)
                    ], CancellationToken.None).ConfigureAwait(false);
                await ExecuteNonQueryAsync(connection, transaction,
                    "INSERT INTO vectors(chunk_id,dimension,vector) VALUES($id,$dimension,$vector);",
                    [("$id", chunk.Id), ("$dimension", vectors[index].Length), ("$vector", Encode(vectors[index]))], CancellationToken.None).ConfigureAwait(false);
            }

            await ExecuteNonQueryAsync(connection, transaction,
                "UPDATE documents SET active_revision_id=$revision WHERE id=$id;",
                [("$revision", revisionId), ("$id", document.Id)], CancellationToken.None).ConfigureAwait(false);
            await InsertFtsRowsAsync(connection, transaction, document.Id, revisionId, chunks, CancellationToken.None).ConfigureAwait(false);

            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            SELECT d.id,d.name,d.description,d.original_file_name,d.media_type,d.size_bytes,d.character_count,d.chunk_count,d.updated_utc,
                   COALESCE(group_concat(t.normalized_tag, char(31)), ''),d.status,
                   (SELECT MAX(c.page_number) FROM chunks c WHERE c.revision_id=d.active_revision_id)
            FROM documents d LEFT JOIN document_tags t ON t.document_id=d.id
            GROUP BY d.id ORDER BY d.updated_utc DESC,d.id;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<KnowledgeDocument>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new KnowledgeDocument(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                SplitTags(reader.GetString(9)), reader.GetString(3), reader.GetString(4), reader.GetInt64(5),
                reader.GetInt32(6), reader.GetInt32(7), DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
                reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetInt32(11)));
        }
        return result;
    }

    public async Task<IReadOnlyList<string>> GetTagCatalogAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT normalized_tag FROM document_tags ORDER BY normalized_tag COLLATE NOCASE;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var tags = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    public async Task<KnowledgeStoredDocument?> GetStoredDocumentAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id,d.name,d.description,d.original_file_name,d.media_type,d.size_bytes,
                   d.character_count,d.chunk_count,d.updated_utc,d.canonical_file_name,
                   COALESCE((SELECT group_concat(t.normalized_tag,char(31)) FROM document_tags t WHERE t.document_id=d.id),''),
                   d.status,(SELECT MAX(c.page_number) FROM chunks c WHERE c.revision_id=d.active_revision_id)
            FROM documents d WHERE d.id=$id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var document = new KnowledgeDocument(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), SplitTags(reader.GetString(10)),
            reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetInt32(7),
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture), reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetInt32(12));
        return new KnowledgeStoredDocument(document, reader.GetString(9));
    }

    public async Task<KnowledgeDocument?> FindActiveBySourceHashAsync(string sourceSha256, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id FROM documents d
            JOIN document_revisions r ON r.id=d.active_revision_id
            WHERE r.source_sha256=$hash LIMIT 1;
            """;
        command.Parameters.AddWithValue("$hash", sourceSha256);
        var id = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return id is null ? null : (await GetStoredDocumentAsync(id, cancellationToken).ConfigureAwait(false))?.Document;
    }

    public async Task<IReadOnlyList<KnowledgeChunkPreview>> GetChunkPreviewAsync(string documentId, int maximumChunks, int maximumCharacters, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.ordinal,c.text,c.estimated_tokens,c.heading,c.page_number
            FROM chunks c JOIN documents d ON d.id=c.document_id
            WHERE d.id=$id AND c.revision_id=d.active_revision_id
            ORDER BY c.ordinal LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$id", documentId);
        command.Parameters.AddWithValue("$limit", maximumChunks);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<KnowledgeChunkPreview>();
        var characters = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var text = reader.GetString(1);
            if (characters + text.Length > maximumCharacters) break;
            result.Add(new KnowledgeChunkPreview(reader.GetInt32(0), text, reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4)));
            characters += text.Length;
        }
        return result;
    }

    public async Task<KnowledgeDocument> UpdateDetailsAsync(
        string id,
        string name,
        string description,
        IReadOnlyList<string> tags,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, transaction,
            "UPDATE documents SET name=$name,description=$description,updated_utc=$updated WHERE id=$id;",
            [("$name", name), ("$description", description), ("$updated", updatedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$id", id)],
            CancellationToken.None).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM document_tags WHERE document_id=$id;", [("$id", id)], CancellationToken.None).ConfigureAwait(false);
        foreach (var tag in tags)
        {
            await ExecuteNonQueryAsync(connection, transaction,
                "INSERT INTO document_tags(document_id,normalized_tag) VALUES($id,$tag);",
                [("$id", id), ("$tag", tag)], CancellationToken.None).ConfigureAwait(false);
        }
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return (await GetStoredDocumentAsync(id, cancellationToken).ConfigureAwait(false))?.Document
            ?? throw new KeyNotFoundException("The Knowledge document no longer exists.");
    }

    public async Task<string> ReplaceAsync(
        KnowledgePreparedIndex prepared,
        KnowledgeOptions options,
        CancellationToken cancellationToken)
    {
        ValidatePrepared(prepared);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT canonical_file_name,active_revision_id FROM documents WHERE id=$id;";
            select.Parameters.AddWithValue("$id", prepared.Document.Id);
            await using var reader = await select.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
            if (!await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
            {
                throw new KeyNotFoundException("The Knowledge document no longer exists.");
            }
            var oldCanonical = reader.GetString(0);
            var oldRevision = reader.IsDBNull(1) ? null : reader.GetString(1);
            await reader.DisposeAsync().ConfigureAwait(false);
            await SetContractAsync(connection, transaction, options, prepared.Vectors[0].Length, false).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM document_tags WHERE document_id=$id;", [("$id", prepared.Document.Id)], CancellationToken.None).ConfigureAwait(false);
            await InsertRevisionAsync(connection, transaction, prepared.RevisionId, prepared.Document, prepared.CanonicalFileName, options, prepared.Vectors[0].Length, CancellationToken.None, prepared.SourceSha256, prepared.ExtractedTextSha256).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, """
                UPDATE documents SET name=$name,description=$description,original_file_name=$file,media_type=$media,
                    size_bytes=$size,character_count=$characters,chunk_count=$chunks,canonical_file_name=$canonical,
                    status='Ready',active_revision_id=$revision,updated_utc=$updated
                WHERE id=$id;
                """, DocumentParameters(prepared).Append(("$revision", prepared.RevisionId)), CancellationToken.None).ConfigureAwait(false);
            await InsertTagsAndIndexAsync(connection, transaction, prepared, CancellationToken.None).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM chunk_fts WHERE document_id=$id;", [("$id", prepared.Document.Id)], CancellationToken.None).ConfigureAwait(false);
            await InsertFtsRowsAsync(connection, transaction, prepared.Document.Id, prepared.RevisionId, prepared.Chunks, CancellationToken.None).ConfigureAwait(false);
            if (!string.Equals(oldCanonical, prepared.CanonicalFileName, StringComparison.OrdinalIgnoreCase))
            {
                await QueueFileCleanupAsync(connection, transaction, oldCanonical).ConfigureAwait(false);
            }
            if (oldRevision is not null)
            {
                await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM document_revisions WHERE id=$revision;", [("$revision", oldRevision)], CancellationToken.None).ConfigureAwait(false);
            }
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return oldCanonical;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ReplaceAllIndexesAsync(
        IReadOnlyList<KnowledgePreparedIndex> preparedDocuments,
        KnowledgeOptions options,
        CancellationToken cancellationToken)
    {
        if (preparedDocuments.Count == 0)
        {
            return;
        }
        foreach (var prepared in preparedDocuments)
        {
            ValidatePrepared(prepared);
        }
        var dimension = preparedDocuments[0].Vectors[0].Length;
        if (preparedDocuments.Any(item => item.Vectors.Any(vector => vector.Length != dimension)))
        {
            throw new InvalidOperationException("The embedding provider returned inconsistent dimensions while rebuilding the library.");
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await SetContractAsync(connection, transaction, options, dimension, true).ConfigureAwait(false);
            foreach (var prepared in preparedDocuments)
            {
                await InsertRevisionAsync(connection, transaction, prepared.RevisionId, prepared.Document, prepared.CanonicalFileName, options, dimension, CancellationToken.None, prepared.SourceSha256, prepared.ExtractedTextSha256).ConfigureAwait(false);
                await ExecuteNonQueryAsync(connection, transaction,
                    "UPDATE documents SET character_count=$characters,chunk_count=$chunks,status='Ready',active_revision_id=$revision,updated_utc=$updated WHERE id=$id;",
                    [("$characters", prepared.Document.CharacterCount), ("$chunks", prepared.Document.ChunkCount), ("$revision", prepared.RevisionId), ("$updated", prepared.Document.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture)), ("$id", prepared.Document.Id)],
                    CancellationToken.None).ConfigureAwait(false);
                await InsertIndexAsync(connection, transaction, prepared, CancellationToken.None).ConfigureAwait(false);
                await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM chunk_fts WHERE document_id=$id;", [("$id", prepared.Document.Id)], CancellationToken.None).ConfigureAwait(false);
                await InsertFtsRowsAsync(connection, transaction, prepared.Document.Id, prepared.RevisionId, prepared.Chunks, CancellationToken.None).ConfigureAwait(false);
            }
            await ExecuteNonQueryAsync(connection, transaction,
                "DELETE FROM document_revisions WHERE id NOT IN (SELECT active_revision_id FROM documents WHERE active_revision_id IS NOT NULL);",
                [], CancellationToken.None).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<(bool Deleted, string? CanonicalFile)> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT canonical_file_name FROM documents WHERE id=$id;";
            select.Parameters.AddWithValue("$id", id);
            var file = await select.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false) as string;
            if (file is null)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return (false, null);
            }

            await QueueFileCleanupAsync(connection, transaction, file).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, "DELETE FROM chunk_fts WHERE document_id=$id;", [("$id", id)], CancellationToken.None).ConfigureAwait(false);
            var deleted = await ExecuteNonQueryCountAsync(
                connection,
                transaction,
                "DELETE FROM documents WHERE id=$id;",
                [("$id", id)],
                CancellationToken.None).ConfigureAwait(false) == 1;
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return (deleted, file);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetPendingFileCleanupAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT canonical_file_name FROM pending_file_cleanup ORDER BY created_utc,canonical_file_name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var files = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            files.Add(reader.GetString(0));
        }

        return files;
    }

    public async Task<IReadOnlySet<string>> GetReferencedCanonicalFilesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT canonical_file_name FROM documents;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            files.Add(reader.GetString(0));
        }

        return files;
    }

    public async Task CompleteFileCleanupAsync(string canonicalFileName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pending_file_cleanup WHERE canonical_file_name=$file;";
        command.Parameters.AddWithValue("$file", canonicalFileName);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task QueueFileCleanupAsync(string canonicalFileName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await QueueFileCleanupAsync(connection, transaction, canonicalFileName).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KnowledgeCandidate>> GetCandidatesAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var parameters = tags.Select((_, index) => $"$tag{index}").ToArray();
        command.CommandText = $"""
            SELECT c.id,c.document_id,d.name,d.description,c.ordinal,c.text,c.estimated_tokens,c.start_character,c.end_character,c.heading,c.page_number,v.dimension,v.vector,
                   (SELECT group_concat(dt.normalized_tag,char(31)) FROM document_tags dt WHERE dt.document_id=d.id)
            FROM chunks c JOIN documents d ON d.id=c.document_id JOIN vectors v ON v.chunk_id=c.id
            WHERE c.revision_id=d.active_revision_id
              AND EXISTS(SELECT 1 FROM document_tags scope WHERE scope.document_id=d.id AND scope.normalized_tag IN ({string.Join(',', parameters)}))
            ORDER BY c.id;
            """;
        for (var index = 0; index < tags.Count; index++)
        {
            command.Parameters.AddWithValue(parameters[index], tags[index]);
        }
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<KnowledgeCandidate>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadCandidate(reader));
        }
        return result;
    }

    public async Task<IReadOnlyList<KnowledgeCandidate>> SearchLexicalCandidatesAsync(IReadOnlyList<string> tags, KnowledgeLexicalQuery query, int maximumCandidates, CancellationToken cancellationToken)
    {
        if (query.IsEmpty) return [];
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var parameters = tags.Select((_, index) => $"$tag{index}").ToArray();
        command.CommandText = $"""
            SELECT c.id,c.document_id,d.name,d.description,c.ordinal,c.text,c.estimated_tokens,c.start_character,c.end_character,c.heading,c.page_number,v.dimension,v.vector,
                   (SELECT group_concat(dt.normalized_tag,char(31)) FROM document_tags dt WHERE dt.document_id=d.id)
            FROM chunk_fts f JOIN chunks c ON c.id=f.chunk_id JOIN documents d ON d.id=c.document_id JOIN vectors v ON v.chunk_id=c.id
            WHERE chunk_fts MATCH $match AND c.revision_id=d.active_revision_id AND f.revision_id=d.active_revision_id
              AND EXISTS(SELECT 1 FROM document_tags scope WHERE scope.document_id=d.id AND scope.normalized_tag IN ({string.Join(',', parameters)}))
            ORDER BY bm25(chunk_fts),c.ordinal,c.id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$match", query.MatchExpression);
        command.Parameters.AddWithValue("$limit", maximumCandidates);
        for (var index = 0; index < tags.Count; index++) command.Parameters.AddWithValue(parameters[index], tags[index]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<KnowledgeCandidate>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadCandidate(reader));
        return result;
    }

    public async Task<IReadOnlyList<KnowledgeCandidate>> GetContiguousContextAsync(string anchorChunkId, int eachSide, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT c.id,c.document_id,d.name,d.description,c.ordinal,c.text,c.estimated_tokens,c.start_character,c.end_character,c.heading,c.page_number,v.dimension,v.vector,(SELECT group_concat(dt.normalized_tag,char(31)) FROM document_tags dt WHERE dt.document_id=d.id) FROM chunks c JOIN documents d ON d.id=c.document_id JOIN vectors v ON v.chunk_id=c.id JOIN chunks a ON a.id=$anchor WHERE c.document_id=a.document_id AND c.revision_id=a.revision_id AND d.active_revision_id=a.revision_id AND c.ordinal BETWEEN a.ordinal-$side AND a.ordinal+$side ORDER BY c.ordinal;""";
        command.Parameters.AddWithValue("$anchor", anchorChunkId); command.Parameters.AddWithValue("$side", eachSide);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<KnowledgeCandidate>(); while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadCandidate(reader)); return result;
    }

    private static KnowledgeCandidate ReadCandidate(SqliteDataReader reader) => new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetInt32(10), Decode((byte[])reader[12], reader.GetInt32(11)), SplitTags(reader.GetString(13)));

    private static async Task InsertFtsRowsAsync(SqliteConnection connection, SqliteTransaction transaction, string documentId, string revisionId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken)
    {
        foreach (var chunk in chunks) await ExecuteNonQueryAsync(connection, transaction, "INSERT INTO chunk_fts(chunk_id,revision_id,document_id,heading,text) VALUES($id,$revision,$document,$heading,$text);", [("$id",chunk.Id),("$revision",revisionId),("$document",documentId),("$heading",chunk.Heading ?? string.Empty),("$text",chunk.Text)], cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static void ValidatePrepared(KnowledgePreparedIndex prepared)
    {
        if (prepared.Chunks.Count == 0 || prepared.Chunks.Count != prepared.Vectors.Count)
        {
            throw new ArgumentException("Every Knowledge chunk requires exactly one vector.", nameof(prepared));
        }
    }

    private static IEnumerable<(string Name, object? Value)> DocumentParameters(KnowledgePreparedIndex prepared) =>
    [
        ("$id",prepared.Document.Id),("$name",prepared.Document.Name),("$description",prepared.Document.Description),
        ("$file",prepared.Document.OriginalFileName),("$media",prepared.Document.MediaType),("$size",prepared.Document.SizeBytes),
        ("$characters",prepared.Document.CharacterCount),("$chunks",prepared.Document.ChunkCount),
        ("$canonical",prepared.CanonicalFileName),("$updated",prepared.Document.UpdatedUtc.ToString("O",CultureInfo.InvariantCulture)),
    ];

    private static async Task InsertTagsAndIndexAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        KnowledgePreparedIndex prepared,
        CancellationToken cancellationToken)
    {
        foreach (var tag in prepared.Document.Tags)
        {
            await ExecuteNonQueryAsync(connection, transaction,
                "INSERT INTO document_tags(document_id,normalized_tag) VALUES($id,$tag);",
                [("$id", prepared.Document.Id), ("$tag", tag)], cancellationToken).ConfigureAwait(false);
        }
        await InsertIndexAsync(connection, transaction, prepared, cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertIndexAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        KnowledgePreparedIndex prepared,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < prepared.Chunks.Count; index++)
        {
            var chunk = prepared.Chunks[index];
            await ExecuteNonQueryAsync(connection, transaction, """
                INSERT INTO chunks(id,document_id,revision_id,ordinal,text,estimated_tokens,start_character,end_character,heading,page_number)
                VALUES($id,$document,$revision,$ordinal,$text,$tokens,$start,$end,$heading,$page);
                """, [("$id",chunk.Id),("$document",prepared.Document.Id),("$revision",prepared.RevisionId),("$ordinal",chunk.Ordinal),("$text",chunk.Text),
                    ("$tokens",chunk.EstimatedTokens),("$start",chunk.StartCharacter),("$end",chunk.EndCharacter),
                    ("$heading",chunk.Heading),("$page",chunk.PageNumber)], cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction,
                "INSERT INTO vectors(chunk_id,dimension,vector) VALUES($id,$dimension,$vector);",
                [("$id", chunk.Id), ("$dimension", prepared.Vectors[index].Length), ("$vector", Encode(prepared.Vectors[index]))],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task InsertRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        KnowledgeDocument document,
        string canonicalFileName,
        KnowledgeOptions options,
        int dimension,
        CancellationToken cancellationToken,
        string sourceSha256 = "",
        string extractedTextSha256 = "") =>
        ExecuteNonQueryAsync(connection, transaction, """
            INSERT INTO document_revisions(
                id,document_id,original_file_name,canonical_file_name,media_type,size_bytes,
                source_sha256,extracted_text_sha256,embedding_provider,embedding_model,dimension,
                chunking_version,index_version,character_count,chunk_count,state,cleanup_pending,created_utc)
            VALUES($id,$document,$original,$canonical,$media,$size,$sourceHash,$textHash,$provider,$model,$dimension,1,1,$characters,$chunks,'Ready',0,$created);
            """, [("$id",revisionId),("$document",document.Id),("$original",document.OriginalFileName),("$canonical",canonicalFileName),
                ("$media",document.MediaType),("$size",document.SizeBytes),("$sourceHash",sourceSha256),("$textHash",extractedTextSha256),
                ("$provider",options.EmbeddingProvider.Trim()),("$model",options.EmbeddingModel.Trim()),("$dimension",dimension),
                ("$characters",document.CharacterCount),("$chunks",document.ChunkCount),("$created",document.UpdatedUtc.ToString("O",CultureInfo.InvariantCulture))],
            cancellationToken);

    public async Task EnsureConfiguredContractAsync(KnowledgeOptions options, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var persisted = await ReadMetadataAsync(connection, cancellationToken).ConfigureAwait(false);
        if (persisted.Count == 0)
        {
            return;
        }

        foreach (var value in BuildContract(options, null))
        {
            if (!persisted.TryGetValue(value.Key, out var actual) ||
                !string.Equals(actual, value.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Knowledge settings differ from the active index contract. Reindex the library.");
            }
        }
    }

    public async Task<KnowledgeIndexStatus> GetIndexStatusAsync(KnowledgeOptions options, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var persisted = await ReadMetadataAsync(connection, cancellationToken).ConfigureAwait(false);
        var configured = BuildContract(options, null);
        var requiresReindex = persisted.Count > 0 && configured.Any(
            value => !persisted.TryGetValue(value.Key, out var actual) ||
                     !string.Equals(actual, value.Value, StringComparison.Ordinal));
        var configuredText = FormatContract(configured);
        var indexedText = persisted.Count == 0 ? null : FormatContract(persisted);
        int? dimension = persisted.TryGetValue("dimension", out var dimensionText) &&
                        int.TryParse(dimensionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
        return new KnowledgeIndexStatus(persisted.Count > 0, requiresReindex, configuredText, indexedText, dimension);
    }

    private static async Task SetContractAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        KnowledgeOptions options,
        int dimension,
        bool replace)
    {
        if (replace)
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                "DELETE FROM metadata;",
                [],
                CancellationToken.None).ConfigureAwait(false);
        }

        var values = BuildContract(options, dimension);
        foreach (var value in values)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO metadata(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value WHERE metadata.value=excluded.value;";
            command.Parameters.AddWithValue("$key", value.Key);
            command.Parameters.AddWithValue("$value", value.Value);
            if (await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException(
                    "Knowledge settings differ from the active index contract. Reindex the library.");
            }
        }
    }

    private static Dictionary<string, string> BuildContract(KnowledgeOptions options, int? dimension)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["embedding_provider"] = options.EmbeddingProvider.Trim().ToLowerInvariant(),
            ["embedding_model"] = options.EmbeddingModel.Trim(),
            ["chunking_version"] = "1",
            ["target_chunk_tokens"] = options.TargetChunkTokens.ToString(CultureInfo.InvariantCulture),
            ["maximum_chunk_tokens"] = options.MaximumChunkTokens.ToString(CultureInfo.InvariantCulture),
            ["overlap_chunk_tokens"] = options.OverlapChunkTokens.ToString(CultureInfo.InvariantCulture),
            ["index_version"] = "1",
            ["distance"] = "cosine",
            ["normalization"] = "unit-l2",
        };
        if (dimension is not null)
        {
            values["dimension"] = dimension.Value.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    private static async Task<Dictionary<string, string>> ReadMetadataAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key,value FROM metadata ORDER BY key;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            values.Add(reader.GetString(0), reader.GetString(1));
        }

        return values;
    }

    private static string FormatContract(IReadOnlyDictionary<string, string> values) =>
        string.Join(", ", values.OrderBy(value => value.Key, StringComparer.Ordinal).Select(value => $"{value.Key}={value.Value}"));

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task<long> ScalarLongAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(token).ConfigureAwait(false) ?? 0L);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IEnumerable<(string Name, object? Value)> values,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values)
        {
            command.Parameters.AddWithValue(value.Name, value.Value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteNonQueryCountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IEnumerable<(string Name, object? Value)> values,
        CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var value in values)
        {
            command.Parameters.AddWithValue(value.Name, value.Value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
    private static Task QueueFileCleanupAsync(SqliteConnection connection, SqliteTransaction transaction, string canonicalFileName) =>
        ExecuteNonQueryAsync(connection, transaction,
            "INSERT INTO pending_file_cleanup(canonical_file_name,created_utc) VALUES($file,$created) ON CONFLICT(canonical_file_name) DO NOTHING;",
            [("$file", canonicalFileName), ("$created", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))],
            CancellationToken.None);
    private static byte[] Encode(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] Decode(byte[] bytes, int dimension)
    {
        if (bytes.Length != dimension * sizeof(float))
        {
            throw new InvalidOperationException("Knowledge vector storage is corrupt.");
        }

        var vector = new float[dimension];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
    private static IReadOnlyList<string> SplitTags(string value) => string.IsNullOrEmpty(value) ? [] : value.Split((char)31, StringSplitOptions.RemoveEmptyEntries);
}

internal sealed record KnowledgeCandidate(string ChunkId, string DocumentId, string DocumentName, string Description, int Ordinal, string Text, int EstimatedTokens, int StartCharacter, int EndCharacter, string? Heading, int? PageNumber, float[] Vector, IReadOnlyList<string> Tags);

internal sealed record KnowledgeStoredDocument(KnowledgeDocument Document, string CanonicalFileName);

internal sealed record KnowledgePreparedIndex(
    KnowledgeDocument Document,
    string CanonicalFileName,
    IReadOnlyList<KnowledgeChunk> Chunks,
    IReadOnlyList<float[]> Vectors,
    string RevisionId,
    string SourceSha256,
    string ExtractedTextSha256);
