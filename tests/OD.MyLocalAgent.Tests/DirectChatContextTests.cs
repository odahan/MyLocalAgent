using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies Direct Chat context projection and incremental compression.</summary>
public sealed class DirectChatContextTests
{
    /// <summary>Verifies summarized messages are replaced by memory while recent turns remain verbatim.</summary>
    [Fact]
    public void ContextBuilderUsesSummaryAndOnlyUnsummarizedMessages()
    {
        var directOptions = Options.Create(new DirectChatOptions());
        var estimator = new ConservativeTokenEstimator(directOptions);
        var aiOptions = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GlobalSystemPrompt = "Global instruction",
                GenerationDefaults = new AIGenerationDefaults
                {
                    ContextWindowTokens = 8192,
                },
            });
        var builder = new DirectChatContextBuilder(estimator, aiOptions, directOptions);
        var conversation = Conversation() with
        {
            SummaryText = "The archived fact",
            SummaryThroughSequence = 2,
        };
        var messages = new[]
        {
            Message(1, DirectChatMessageRole.User, "old user"),
            Message(2, DirectChatMessageRole.Assistant, "old assistant"),
            Message(3, DirectChatMessageRole.User, "recent user"),
        };

        var plan = builder.Build(conversation, messages);

        plan.Messages.Should().Contain(message => message.Text!.Contains("The archived fact"));
        plan.Messages.Should().Contain(message => message.Text == "recent user");
        plan.Messages.Should().NotContain(message => message.Text == "old user");
        plan.Messages.Should().NotContain(message => message.Text == "old assistant");
        plan.Messages.Should().Contain(message => message.Text == "Global instruction");
    }

    /// <summary>Verifies compression advances through old complete turns and keeps four recent user turns.</summary>
    [Fact]
    public async Task CompressorSummarizesOnlyTurnsBeforeRecentRetentionWindow()
    {
        var store = new Mock<IDirectChatStore>(MockBehavior.Strict);
        store.Setup(instance => instance.UpdateSummaryAsync(
                "abc",
                "compressed memory",
                2,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var chatClient = new StubChatClient("compressed memory");
        var provider = new StubLlmClientService(chatClient, "Stub");
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var aiOptions = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults
                {
                    ContextWindowTokens = 8192,
                },
            });
        var compressor = new DirectChatHistoryCompressor(
            store.Object,
            factory.Object,
            aiOptions,
            new ConservativeTokenEstimator(Options.Create(new DirectChatOptions())),
            Options.Create(new DirectChatOptions { MinimumRecentTurns = 4 }));
        var messages = Enumerable.Range(1, 5)
            .SelectMany(turn => new[]
            {
                Message(turn * 2 - 1, DirectChatMessageRole.User, $"user {turn}"),
                Message(turn * 2, DirectChatMessageRole.Assistant, $"assistant {turn}"),
            })
            .ToArray();

        var changed = await compressor.CompressAsync(
            Conversation(),
            messages,
            CancellationToken.None);

        changed.Should().BeTrue();
        chatClient.LastMessages.Single().Text.Should().Contain("user 1");
        chatClient.LastMessages.Single().Text.Should().Contain("assistant 1");
        chatClient.LastMessages.Single().Text.Should().NotContain("user 2");
        store.VerifyAll();
        factory.VerifyAll();
    }

    private static DirectChatConversation Conversation() =>
        new()
        {
            Id = "abc",
            Title = "Chat",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };

    private static DirectChatMessage Message(
        long sequence,
        DirectChatMessageRole role,
        string content) =>
        new()
        {
            Id = sequence.ToString("x32"),
            ConversationId = "abc",
            Sequence = sequence,
            Role = role,
            Content = content,
            State = DirectChatMessageState.Completed,
            CreatedUtc = DateTimeOffset.UtcNow,
        };
}
