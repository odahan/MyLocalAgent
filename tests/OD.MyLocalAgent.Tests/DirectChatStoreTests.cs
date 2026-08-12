using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;
using Microsoft.Data.Sqlite;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the dedicated SQLite persistence used by Direct Chat.</summary>
public sealed class DirectChatStoreTests
{
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    /// <summary>Verifies durable CRUD, summary metadata, interruption recovery, and cascading deletion.</summary>
    [Fact]
    public async Task StorePersistsConversationAndRecoversInterruptedResponse()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "direct-chat.db");
        try
        {
            DirectChatConversation conversation;
            DirectChatMessage assistant;
            using (var store = new SqliteDirectChatStore(path))
            {
                await store.InitializeAsync(CancellationToken.None);
                conversation = await store.CreateConversationAsync(
                    "New chat",
                    CancellationToken.None);
                var user = await store.AppendMessageAsync(
                    conversation.Id,
                    DirectChatMessageRole.User,
                    "Remember the blue folder.",
                    DirectChatMessageState.Completed,
                    6,
                    CancellationToken.None);
                assistant = await store.AppendMessageAsync(
                    conversation.Id,
                    DirectChatMessageRole.Assistant,
                    "I will remember",
                    DirectChatMessageState.Streaming,
                    4,
                    CancellationToken.None);
                await store.UpdateSummaryAsync(
                    conversation.Id,
                    "The user mentioned a blue folder.",
                    user.Sequence,
                    CancellationToken.None);
            }

            using (var reopened = new SqliteDirectChatStore(path))
            {
                await reopened.InitializeAsync(CancellationToken.None);
                var persisted = await reopened.GetConversationAsync(
                    conversation.Id,
                    CancellationToken.None);
                var messages = await reopened.GetMessagesAsync(
                    conversation.Id,
                    null,
                    100,
                    CancellationToken.None);

                persisted.Should().NotBeNull();
                persisted!.SummaryText.Should().Be("The user mentioned a blue folder.");
                persisted.SummaryThroughSequence.Should().Be(1);
                messages.Should().HaveCount(2);
                messages.Single(message => message.Id == assistant.Id)
                    .State.Should().Be(DirectChatMessageState.Failed);

                (await reopened.DeleteConversationAsync(
                    conversation.Id,
                    CancellationToken.None)).Should().BeTrue();
                (await reopened.GetMessagesAsync(
                    conversation.Id,
                    null,
                    100,
                    CancellationToken.None)).Should().BeEmpty();
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Verifies ordered image and audio parts survive a complete reopen.</summary>
    [Fact]
    public async Task StorePersistsOrderedMultimodalContents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "direct-chat.db");
        try
        {
            string conversationId;
            using (var store = new SqliteDirectChatStore(path))
            {
                await store.InitializeAsync(CancellationToken.None);
                var conversation = await store.CreateConversationAsync("Media", CancellationToken.None);
                conversationId = conversation.Id;
                await store.AppendMessageAsync(
                    conversation.Id,
                    DirectChatMessageRole.Assistant,
                    "Caption",
                    DirectChatMessageState.Completed,
                    2,
                    CancellationToken.None,
                    contents:
                    [
                        new DirectChatContent { Position = 0, Kind = DirectChatContentKind.Text, MediaType = "text/markdown", Text = "Caption", SizeBytes = 7 },
                        new DirectChatContent { Position = 1, Kind = DirectChatContentKind.Image, MediaType = "image/png", FileName = "image.png", Data = PngBytes, SizeBytes = PngBytes.Length, IsCloudContent = true, ProviderName = "Test cloud" },
                        new DirectChatContent { Position = 2, Kind = DirectChatContentKind.Audio, MediaType = "audio/wav", FileName = "speech.wav", Data = [1, 2, 3], SizeBytes = 3, Duration = TimeSpan.FromSeconds(2) },
                    ]);
            }

            using var reopened = new SqliteDirectChatStore(path);
            var message = (await reopened.GetMessagesAsync(conversationId, null, 10, CancellationToken.None)).Single();
            message.Content.Should().Be("Caption");
            message.Contents.Select(static item => item.Kind).Should().Equal(
                DirectChatContentKind.Text,
                DirectChatContentKind.Image,
                DirectChatContentKind.Audio);
            message.Contents[1].Data.Should().Equal(PngBytes);
            message.Contents[1].IsCloudContent.Should().BeTrue();
            message.Contents[2].Duration.Should().Be(TimeSpan.FromSeconds(2));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies local access rules are stored with the agent profile of a conversation.</summary>
    [Fact]
    public async Task StorePersistsConversationAuthorizedLocations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "direct-chat.db");
        try
        {
            using var store = new SqliteDirectChatStore(path);
            var conversation = await store.CreateConversationAsync("Locations", CancellationToken.None);
            var location = Path.Combine(directory, "authorized");

            await store.UpdateConversationAgentProfileAsync(
                conversation.Id,
                agentEnabled: true,
                [ToolGroups.FileSystem],
                [],
                [new FileSystemAccessRule { Path = location, Access = FileSystemAccessMode.ReadOnly }],
                CancellationToken.None);

            var persisted = await store.GetConversationAsync(conversation.Id, CancellationToken.None);
            persisted!.AgentEnabled.Should().BeTrue();
            persisted.FileSystemAccess.Should().ContainSingle();
            persisted.FileSystemAccess[0].Path.Should().Be(location);
            persisted.FileSystemAccess[0].Access.Should().Be(FileSystemAccessMode.ReadOnly);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies schema-v3 text rows migrate automatically without text changes.</summary>
    [Fact]
    public async Task VersionThreeTextConversationMigratesWithoutManualAction()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "direct-chat.db");
        const string conversationId = "0123456789abcdef0123456789abcdef";
        const string messageId = "abcdef0123456789abcdef0123456789";
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    CREATE TABLE conversations (id TEXT PRIMARY KEY, title TEXT NOT NULL, created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, summary_text TEXT NULL, summary_through_sequence INTEGER NULL, summary_updated_utc TEXT NULL, agent_enabled INTEGER NOT NULL DEFAULT 0, tools_json TEXT NULL, capabilities_json TEXT NULL);
                    CREATE TABLE messages (id TEXT PRIMARY KEY, conversation_id TEXT NOT NULL, sequence INTEGER NOT NULL, role TEXT NOT NULL, content TEXT NOT NULL, state TEXT NOT NULL, created_utc TEXT NOT NULL, estimated_tokens INTEGER NULL, attachments_json TEXT NULL, FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE);
                    INSERT INTO conversations (id, title, created_utc, updated_utc) VALUES ('{conversationId}', 'Legacy', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
                    INSERT INTO messages (id, conversation_id, sequence, role, content, state, created_utc) VALUES ('{messageId}', '{conversationId}', 1, 'Assistant', '**Legacy text**', 'Completed', '2026-01-01T00:00:00.0000000+00:00');
                    PRAGMA user_version=3;
                    """;
                await command.ExecuteNonQueryAsync();
            }

            using (var store = new SqliteDirectChatStore(path))
            {
                var message = (await store.GetMessagesAsync(conversationId, null, 10, CancellationToken.None)).Single();
                message.Content.Should().Be("**Legacy text**");
                message.Contents.Should().ContainSingle();
                message.Contents[0].Text.Should().Be("**Legacy text**");
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
