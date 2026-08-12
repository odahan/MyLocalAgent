using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OD.MyLocalAgent.Core;

/// <summary>Persists Direct Chat data in one dedicated SQLite database.</summary>
public sealed class SqliteDirectChatStore : IDirectChatStore, IDisposable
{
    private const int CurrentSchemaVersion = 3;
    private readonly string _databasePath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    /// <summary>Initializes a store at the standard per-user database path.</summary>
    public SqliteDirectChatStore(TimeProvider timeProvider)
        : this(LocalAppPaths.DirectChatDatabasePath, timeProvider)
    {
    }

    /// <summary>Initializes a store at an explicit database path.</summary>
    public SqliteDirectChatStore(
        string databasePath,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        SqliteNativeBootstrap.Initialize();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath)
                ?? throw new InvalidOperationException(
                    "The Direct Chat database path has no parent directory.");
            Directory.CreateDirectory(directory);
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                    connection,
                    "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;",
                    cancellationToken)
                .ConfigureAwait(false);

            var version = await GetSchemaVersionAsync(connection, cancellationToken)
                .ConfigureAwait(false);
            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Direct Chat database schema {version} is newer than supported schema {CurrentSchemaVersion}.");
            }

            if (version == 0)
            {
                using var transaction = connection.BeginTransaction();
                await ExecuteNonQueryAsync(
                        connection,
                        """
                        CREATE TABLE IF NOT EXISTS conversations (
                            id TEXT PRIMARY KEY,
                            title TEXT NOT NULL,
                            created_utc TEXT NOT NULL,
                            updated_utc TEXT NOT NULL,
                            summary_text TEXT NULL,
                            summary_through_sequence INTEGER NULL,
                            summary_updated_utc TEXT NULL
                        );
                        CREATE TABLE IF NOT EXISTS messages (
                            id TEXT PRIMARY KEY,
                            conversation_id TEXT NOT NULL,
                            sequence INTEGER NOT NULL,
                            role TEXT NOT NULL CHECK (role IN ('User', 'Assistant')),
                            content TEXT NOT NULL,
                            state TEXT NOT NULL CHECK (state IN ('Pending', 'Streaming', 'Completed', 'Canceled', 'Failed')),
                            created_utc TEXT NOT NULL,
                            estimated_tokens INTEGER NULL,
                            FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE,
                            UNIQUE (conversation_id, sequence)
                        );
                        CREATE INDEX IF NOT EXISTS ix_conversations_updated
                            ON conversations(updated_utc DESC);
                        CREATE INDEX IF NOT EXISTS ix_messages_conversation_sequence
                            ON messages(conversation_id, sequence);
                        PRAGMA user_version=1;
                        """,
                        cancellationToken,
                        transaction)
                    .ConfigureAwait(false);
                transaction.Commit();
            }

            if (version < 2)
            {
                using var transaction = connection.BeginTransaction();
                await ExecuteNonQueryAsync(
                        connection,
                        "ALTER TABLE messages ADD COLUMN attachments_json TEXT NULL; PRAGMA user_version=2;",
                        cancellationToken,
                        transaction)
                    .ConfigureAwait(false);
                transaction.Commit();
            }

            if (version < 3)
            {
                using var transaction = connection.BeginTransaction();
                await ExecuteNonQueryAsync(
                        connection,
                        "ALTER TABLE conversations ADD COLUMN agent_enabled INTEGER NOT NULL DEFAULT 0; ALTER TABLE conversations ADD COLUMN tools_json TEXT NULL; ALTER TABLE conversations ADD COLUMN capabilities_json TEXT NULL; PRAGMA user_version=3;",
                        cancellationToken,
                        transaction)
                    .ConfigureAwait(false);
                transaction.Commit();
            }

            await ExecuteNonQueryAsync(
                    connection,
                    "UPDATE messages SET state = 'Failed' WHERE state IN ('Pending', 'Streaming');",
                    cancellationToken)
                .ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectChatConversation>> GetConversationsAsync(
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = ConversationSelect + " ORDER BY updated_utc DESC, id ASC";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            var conversations = new List<DirectChatConversation>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                conversations.Add(ReadConversation(reader));
            }

            return conversations;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DirectChatConversation?> GetConversationAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(conversationId, nameof(conversationId));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = ConversationSelect + " WHERE id = $id";
            command.Parameters.AddWithValue("$id", conversationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadConversation(reader)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DirectChatConversation> CreateConversationAsync(
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var conversation = new DirectChatConversation
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = NormalizeTitle(title),
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO conversations (id, title, created_utc, updated_utc) VALUES ($id, $title, $created, $updated)";
            command.Parameters.AddWithValue("$id", conversation.Id);
            command.Parameters.AddWithValue("$title", conversation.Title);
            command.Parameters.AddWithValue("$created", FormatDate(conversation.CreatedUtc));
            command.Parameters.AddWithValue("$updated", FormatDate(conversation.UpdatedUtc));
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return conversation;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateConversationTitleAsync(
        string conversationId,
        string title,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(conversationId, nameof(conversationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE conversations SET title = $title, updated_utc = $updated WHERE id = $id";
            command.Parameters.AddWithValue("$title", NormalizeTitle(title));
            command.Parameters.AddWithValue("$updated", FormatDate(_timeProvider.GetUtcNow()));
            command.Parameters.AddWithValue("$id", conversationId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException("The Direct Chat conversation no longer exists.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateConversationAgentProfileAsync(
        string conversationId,
        bool agentEnabled,
        IReadOnlyList<string> tools,
        IReadOnlyList<TaskCapability> capabilities,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(conversationId, nameof(conversationId));
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(capabilities);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE conversations SET agent_enabled = $enabled, tools_json = $tools, capabilities_json = $capabilities, updated_utc = $updated WHERE id = $id";
            command.Parameters.AddWithValue("$enabled", agentEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$tools", tools.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(tools));
            command.Parameters.AddWithValue("$capabilities", capabilities.Count == 0 ? DBNull.Value : JsonSerializer.Serialize(capabilities));
            command.Parameters.AddWithValue("$updated", FormatDate(_timeProvider.GetUtcNow()));
            command.Parameters.AddWithValue("$id", conversationId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
                throw new InvalidOperationException("The Direct Chat conversation no longer exists.");
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteConversationAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(conversationId, nameof(conversationId));
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM conversations WHERE id = $id";
            command.Parameters.AddWithValue("$id", conversationId);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectChatMessage>> GetMessagesAsync(
        string conversationId,
        long? beforeSequence,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(conversationId, nameof(conversationId));
        if (maximumCount <= 0 || maximumCount > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, conversation_id, sequence, role, content, state, created_utc, estimated_tokens, attachments_json
                FROM messages
                WHERE conversation_id = $conversationId
                  AND ($before IS NULL OR sequence < $before)
                ORDER BY sequence DESC
                LIMIT $limit
                """;
            command.Parameters.AddWithValue("$conversationId", conversationId);
            command.Parameters.AddWithValue("$before", (object?)beforeSequence ?? DBNull.Value);
            command.Parameters.AddWithValue("$limit", maximumCount);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            var messages = new List<DirectChatMessage>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(ReadMessage(reader));
            }

            messages.Reverse();
            return messages;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DirectChatMessage> AppendMessageAsync(
        string conversationId,
        DirectChatMessageRole role,
        string content,
        DirectChatMessageState state,
        int? estimatedTokens,
        CancellationToken cancellationToken,
        IReadOnlyList<DirectChatAttachment>? attachments = null)
    {
        ValidateIdentifier(conversationId, nameof(conversationId));
        ArgumentNullException.ThrowIfNull(content);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            long sequence;
            using (var sequenceCommand = connection.CreateCommand())
            {
                sequenceCommand.Transaction = transaction;
                sequenceCommand.CommandText =
                    "SELECT COALESCE(MAX(sequence), 0) + 1 FROM messages WHERE conversation_id = $conversationId";
                sequenceCommand.Parameters.AddWithValue("$conversationId", conversationId);
                sequence = Convert.ToInt64(
                    await sequenceCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
            }

            var message = new DirectChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                ConversationId = conversationId,
                Sequence = sequence,
                Role = role,
                Content = content,
                State = state,
                CreatedUtc = _timeProvider.GetUtcNow(),
                EstimatedTokens = estimatedTokens,
                Attachments = attachments?.ToArray() ?? [],
            };
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO messages
                        (id, conversation_id, sequence, role, content, state, created_utc, estimated_tokens, attachments_json)
                    VALUES
                        ($id, $conversationId, $sequence, $role, $content, $state, $created, $tokens, $attachments)
                    """;
                AddMessageParameters(insert, message);
                _ = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await TouchConversationAsync(
                    connection,
                    transaction,
                    conversationId,
                    message.CreatedUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            transaction.Commit();
            return message;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateMessageAsync(
        string messageId,
        string content,
        DirectChatMessageState state,
        int? estimatedTokens,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(messageId, nameof(messageId));
        ArgumentNullException.ThrowIfNull(content);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var transaction = connection.BeginTransaction();
            string? conversationId;
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText =
                    """
                    UPDATE messages
                    SET content = $content, state = $state, estimated_tokens = $tokens
                    WHERE id = $id
                    RETURNING conversation_id
                    """;
                update.Parameters.AddWithValue("$content", content);
                update.Parameters.AddWithValue("$state", state.ToString());
                update.Parameters.AddWithValue("$tokens", (object?)estimatedTokens ?? DBNull.Value);
                update.Parameters.AddWithValue("$id", messageId);
                conversationId = Convert.ToString(
                    await update.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(conversationId))
            {
                throw new InvalidOperationException("The Direct Chat message no longer exists.");
            }

            await TouchConversationAsync(
                    connection,
                    transaction,
                    conversationId,
                    _timeProvider.GetUtcNow(),
                    cancellationToken)
                .ConfigureAwait(false);
            transaction.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateSummaryAsync(
        string conversationId,
        string summary,
        long throughSequence,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(conversationId, nameof(conversationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (throughSequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(throughSequence));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE conversations
                SET summary_text = $summary,
                    summary_through_sequence = $through,
                    summary_updated_utc = $updated,
                    updated_utc = $updated
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$summary", summary.Trim());
            command.Parameters.AddWithValue("$through", throughSequence);
            command.Parameters.AddWithValue("$updated", FormatDate(_timeProvider.GetUtcNow()));
            command.Parameters.AddWithValue("$id", conversationId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException("The Direct Chat conversation no longer exists.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the store synchronization primitive.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
    }

    private const string ConversationSelect =
        "SELECT id, title, created_utc, updated_utc, summary_text, summary_through_sequence, summary_updated_utc, agent_enabled, tools_json, capabilities_json FROM conversations";

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DirectChatConversation ReadConversation(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            CreatedUtc = ParseDate(reader.GetString(2)),
            UpdatedUtc = ParseDate(reader.GetString(3)),
            SummaryText = reader.IsDBNull(4) ? null : reader.GetString(4),
            SummaryThroughSequence = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            SummaryUpdatedUtc = reader.IsDBNull(6)
                ? null
                : ParseDate(reader.GetString(6)),
            AgentEnabled = !reader.IsDBNull(7) && reader.GetInt64(7) != 0,
            Tools = reader.IsDBNull(8)
                ? []
                : JsonSerializer.Deserialize<List<string>>(reader.GetString(8)) ?? [],
            Capabilities = reader.IsDBNull(9)
                ? []
                : JsonSerializer.Deserialize<List<TaskCapability>>(reader.GetString(9)) ?? [],
        };

    private static DirectChatMessage ReadMessage(SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetString(0),
            ConversationId = reader.GetString(1),
            Sequence = reader.GetInt64(2),
            Role = Enum.Parse<DirectChatMessageRole>(reader.GetString(3)),
            Content = reader.GetString(4),
            State = Enum.Parse<DirectChatMessageState>(reader.GetString(5)),
            CreatedUtc = ParseDate(reader.GetString(6)),
            EstimatedTokens = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            Attachments = reader.IsDBNull(8)
                ? []
                : JsonSerializer.Deserialize<List<DirectChatAttachment>>(reader.GetString(8)) ?? [],
        };

    private static void AddMessageParameters(
        SqliteCommand command,
        DirectChatMessage message)
    {
        command.Parameters.AddWithValue("$id", message.Id);
        command.Parameters.AddWithValue("$conversationId", message.ConversationId);
        command.Parameters.AddWithValue("$sequence", message.Sequence);
        command.Parameters.AddWithValue("$role", message.Role.ToString());
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$state", message.State.ToString());
        command.Parameters.AddWithValue("$created", FormatDate(message.CreatedUtc));
        command.Parameters.AddWithValue(
            "$tokens",
            (object?)message.EstimatedTokens ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$attachments",
            message.Attachments.Count == 0
                ? DBNull.Value
                : JsonSerializer.Serialize(message.Attachments));
    }

    private static async Task TouchConversationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string conversationId,
        DateTimeOffset updatedUtc,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE conversations SET updated_utc = $updated WHERE id = $id";
        command.Parameters.AddWithValue("$updated", FormatDate(updatedUtc));
        command.Parameters.AddWithValue("$id", conversationId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException("The Direct Chat conversation no longer exists.");
        }
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = string.Join(
            ' ',
            title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 80 ? normalized : normalized[..77] + "…";
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(static character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException(
                "Direct Chat identifiers must contain hexadecimal characters only.",
                parameterName);
        }
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
