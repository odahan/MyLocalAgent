using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the Direct Chat interactive streaming surface.</summary>
public sealed class DirectChatViewModelTests
{
    /// <summary>Verifies a user message and streamed assistant reply are persisted to SQLite.</summary>
    [Fact]
    public async Task SendStreamsAndPersistsCompletedConversation()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "direct-chat.db");
        try
        {
            using var store = new SqliteDirectChatStore(path);
            var directOptions = Options.Create(new DirectChatOptions());
            var estimator = new ConservativeTokenEstimator(directOptions);
            var aiOptions = new MutableOptionsMonitor<AIOptions>(
                new AIOptions
                {
                    GenerationDefaults = new AIGenerationDefaults
                    {
                        ContextWindowTokens = 8192,
                    },
                });
            var contextBuilder = new DirectChatContextBuilder(
                estimator,
                aiOptions,
                directOptions);
            var compressor = new Mock<IDirectChatHistoryCompressor>(MockBehavior.Strict);
            var provider = new StubLlmClientService(
                new StubChatClient("Hello from the model."),
                "Stub");
            var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
            factory.Setup(instance => instance.Create()).Returns(provider);
            var webView = new Mock<IWebView2AvailabilityService>(MockBehavior.Strict);
            webView.Setup(instance => instance.IsAvailable()).Returns(false);
            var viewModel = new DirectChatViewModel(
                store,
                contextBuilder,
                compressor.Object,
                estimator,
                factory.Object,
                aiOptions,
                Mock.Of<IIncidentReporter>(),
                Mock.Of<IDesktopInteractionService>(),
                new MarkdownRenderingService(),
                webView.Object);

            await viewModel.LoadCommand.ExecuteAsync(null);
            viewModel.DraftMessage = "Hello";
            await viewModel.SendCommand.ExecuteAsync(null);

            viewModel.Messages.Should().HaveCount(2);
            viewModel.Messages[0].Content.Should().Be("Hello");
            viewModel.Messages[1].Content.Should().Be("Hello from the model.");
            viewModel.Messages[1].State.Should().Be(DirectChatMessageState.Completed);
            viewModel.SelectedConversation!.Title.Should().Be("Hello");
            var persisted = await store.GetMessagesAsync(
                viewModel.SelectedConversation.Id,
                null,
                100,
                CancellationToken.None);
            persisted.Select(message => message.State).Should().Equal(
                DirectChatMessageState.Completed,
                DirectChatMessageState.Completed);
            compressor.VerifyNoOtherCalls();
            factory.Verify(instance => instance.Create(), Times.AtLeastOnce);
            webView.VerifyAll();
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
