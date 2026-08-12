using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the dedicated SQLite persistence used by Direct Chat.</summary>
public sealed class DirectChatStoreTests
{
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
}
