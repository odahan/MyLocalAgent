using FluentAssertions;
using CommunityToolkit.Mvvm.Messaging;
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
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    /// <summary>Verifies application-owned workspace data cannot be added as a chat grant.</summary>
    [Fact]
    public void ApplicationOwnedLocationCannotBeAuthorized()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.DirectChat",
            Guid.NewGuid().ToString("N"));
        var reserved = Path.Combine(
            workspace,
            WorkspacePaths.ApplicationDirectoryName,
            "data");
        Directory.CreateDirectory(reserved);
        try
        {
            var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
            desktop.Setup(instance => instance.SelectFolder(
                    "Select an authorized Direct Chat location"))
                .Returns(reserved);
            var webView = new Mock<IWebView2AvailabilityService>(MockBehavior.Strict);
            webView.Setup(instance => instance.IsAvailable()).Returns(false);
            var aiOptions = new MutableOptionsMonitor<AIOptions>(new AIOptions());
            var directOptions = Options.Create(new DirectChatOptions());
            var estimator = new ConservativeTokenEstimator(directOptions);
            var sandbox = new PathSandbox(
                Options.Create(new FileSystemOptions { RootPath = workspace }));
            var viewModel = new DirectChatViewModel(
                Mock.Of<IDirectChatStore>(),
                new DirectChatContextBuilder(estimator, aiOptions, directOptions),
                Mock.Of<IDirectChatHistoryCompressor>(),
                estimator,
                Mock.Of<ILlmClientFactory>(),
                Mock.Of<IMyLocalAgentService>(),
                Mock.Of<ITaskStore>(),
                WeakReferenceMessenger.Default,
                aiOptions,
                Mock.Of<IIncidentReporter>(),
                desktop.Object,
                new MarkdownRenderingService(),
                webView.Object,
                new DirectChatMediaFileService(),
                sandbox);

            viewModel.AddAuthorizedLocationCommand.Execute(null);

            viewModel.AuthorizedLocations.Should().BeEmpty();
            viewModel.StatusText.Should().Contain("reserved for application data");
            desktop.VerifyAll();
            webView.VerifyAll();
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

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
                new StubChatClient(
                    ["Hello from the model.", "The second response."]),
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
                Mock.Of<IMyLocalAgentService>(),
                Mock.Of<ITaskStore>(),
                WeakReferenceMessenger.Default,
                aiOptions,
                Mock.Of<IIncidentReporter>(),
                Mock.Of<IDesktopInteractionService>(),
                new MarkdownRenderingService(),
                webView.Object,
                new DirectChatMediaFileService());

            await viewModel.LoadCommand.ExecuteAsync(null);
            var metadataMoveNotifications = 0;
            viewModel.Conversations.CollectionChanged += (_, eventArgs) =>
            {
                if (eventArgs.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
                {
                    metadataMoveNotifications++;
                }
            };
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

            viewModel.DraftMessage = "Can you answer a second time?";
            viewModel.SendCommand.CanExecute(null).Should().BeTrue();
            await viewModel.SendCommand.ExecuteAsync(null);

            viewModel.Messages.Should().HaveCount(4);
            viewModel.Messages[^1].Content.Should().Be("The second response.");
            viewModel.Messages[^1].State.Should().Be(DirectChatMessageState.Completed);
            metadataMoveNotifications.Should().Be(0);
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

    /// <summary>Verifies visible reasoning stays separate from the persisted assistant answer.</summary>
    [Fact]
    public async Task SendDisplaysReasoningWithoutPersistingIt()
    {
        var chatClient = new StubChatClient("unused")
        {
            StreamingUpdates =
            [
                new Microsoft.Extensions.AI.ChatResponseUpdate
                {
                    Role = Microsoft.Extensions.AI.ChatRole.Assistant,
                    Contents = [new Microsoft.Extensions.AI.TextReasoningContent("Private reasoning")],
                },
                new Microsoft.Extensions.AI.ChatResponseUpdate(
                    Microsoft.Extensions.AI.ChatRole.Assistant,
                    "Visible answer"),
            ],
        };
        await using var context = await CreateViewModelAsync(
            new StubLlmClientService(chatClient, "Stub"));
        context.ViewModel.DraftMessage = "Question";

        await context.ViewModel.SendCommand.ExecuteAsync(null);

        context.ViewModel.ReasoningText.Should().Be("Private reasoning");
        context.ViewModel.HasReasoning.Should().BeTrue();
        context.ViewModel.Messages[1].Content.Should().Be("Visible answer");
        var persisted = await context.Store.GetMessagesAsync(
            context.ViewModel.SelectedConversation!.Id,
            null,
            10,
            CancellationToken.None);
        persisted[1].Content.Should().Be("Visible answer");
        persisted[1].Content.Should().NotContain("Private reasoning");
    }

    /// <summary>Verifies transient reasoning does not remain visible after changing conversations.</summary>
    [Fact]
    public async Task NewConversationClearsVisibleReasoning()
    {
        var chatClient = new StubChatClient("unused")
        {
            StreamingUpdates =
            [
                new Microsoft.Extensions.AI.ChatResponseUpdate
                {
                    Role = Microsoft.Extensions.AI.ChatRole.Assistant,
                    Contents = [new Microsoft.Extensions.AI.TextReasoningContent("Private reasoning")],
                },
                new Microsoft.Extensions.AI.ChatResponseUpdate(
                    Microsoft.Extensions.AI.ChatRole.Assistant,
                    "Visible answer"),
            ],
        };
        await using var context = await CreateViewModelAsync(
            new StubLlmClientService(chatClient, "Stub"));
        context.ViewModel.DraftMessage = "Question";

        await context.ViewModel.SendCommand.ExecuteAsync(null);
        await context.ViewModel.NewConversationCommand.ExecuteAsync(null);

        context.ViewModel.ReasoningText.Should().BeEmpty();
        context.ViewModel.HasReasoning.Should().BeFalse();
    }

    /// <summary>Verifies image generation projects and persists a cloud media response.</summary>
    [Fact]
    public async Task GenerateImagePersistsAndProjectsMedia()
    {
        var provider = new StubLlmClientService("unused")
        {
            Capabilities = ProviderCapabilities.TextChat | ProviderCapabilities.ImageOutput,
            MediaHandler = (request, _) => Task.FromResult(new MultimodalGenerationResult
            {
                Kind = request.Kind,
                MediaType = "image/png",
                FileName = "generated.png",
                Data = PngBytes,
            }),
        };
        await using var context = await CreateViewModelAsync(provider);
        context.ViewModel.DraftMessage = "A small blue square";

        await context.ViewModel.GenerateImageCommand.ExecuteAsync(null);

        context.ViewModel.Messages.Should().HaveCount(2);
        var assistant = context.ViewModel.Messages[1];
        assistant.State.Should().Be(DirectChatMessageState.Completed);
        assistant.Contents.Should().ContainSingle(static item => item.IsImage);
        assistant.Contents[0].CloudDisclosure.Should().Contain("Stub");
        var persisted = await context.Store.GetMessagesAsync(
            context.ViewModel.SelectedConversation!.Id,
            null,
            10,
            CancellationToken.None);
        persisted[1].Contents[0].Data.Should().Equal(PngBytes);
    }

    /// <summary>Verifies cancellation is visible and durable for an asynchronous media request.</summary>
    [Fact]
    public async Task GenerateAudioCanBeCanceled()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new StubLlmClientService("unused")
        {
            Capabilities = ProviderCapabilities.TextChat | ProviderCapabilities.AudioOutput,
            MediaHandler = async (_, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException();
            },
        };
        await using var context = await CreateViewModelAsync(provider);
        context.ViewModel.DraftMessage = "Read this aloud";
        var generation = context.ViewModel.GenerateAudioCommand.ExecuteAsync(null);
        await started.Task;

        context.ViewModel.GenerateAudioCancelCommand.Execute(null);
        await generation;

        context.ViewModel.Messages[1].State.Should().Be(DirectChatMessageState.Canceled);
        var persisted = await context.Store.GetMessagesAsync(
            context.ViewModel.SelectedConversation!.Id,
            null,
            10,
            CancellationToken.None);
        persisted[1].State.Should().Be(DirectChatMessageState.Canceled);
    }

    /// <summary>Verifies provider failures are shown, persisted, and reported without raw media.</summary>
    [Fact]
    public async Task GenerateImageFailureMarksAssistantMessageFailed()
    {
        var provider = new StubLlmClientService("unused")
        {
            Capabilities = ProviderCapabilities.TextChat | ProviderCapabilities.ImageOutput,
            MediaHandler = (_, _) => Task.FromException<MultimodalGenerationResult>(
                new HttpRequestException("Provider returned HTTP 503.")),
        };
        await using var context = await CreateViewModelAsync(provider);
        context.ViewModel.DraftMessage = "Unavailable image";

        await context.ViewModel.GenerateImageCommand.ExecuteAsync(null);

        context.ViewModel.Messages[1].State.Should().Be(DirectChatMessageState.Failed);
        context.IncidentReporter.Reports.Should().ContainSingle();
        context.IncidentReporter.Reports[0].Source.Should().Be("Direct Chat Image generation");
    }

    /// <summary>Verifies a transient WPF deselection keeps the active conversation available.</summary>
    [Fact]
    public async Task TransientDeselectionRestoresCurrentConversation()
    {
        await using var context = await CreateViewModelAsync(new StubLlmClientService("unused"));
        var selected = context.ViewModel.SelectedConversation;

        context.ViewModel.SelectedConversation = null;

        context.ViewModel.SelectedConversation.Should().BeSameAs(selected);
        context.ViewModel.AttachFileCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>Verifies a provider completion without text is visible, durable, and reported.</summary>
    [Fact]
    public async Task EmptyProviderResponseIsPersistedAsFailure()
    {
        await using var context = await CreateViewModelAsync(new StubLlmClientService(string.Empty));
        context.ViewModel.DraftMessage = "Answer this";

        await context.ViewModel.SendCommand.ExecuteAsync(null);

        var assistant = context.ViewModel.Messages[1];
        assistant.State.Should().Be(DirectChatMessageState.Failed);
        assistant.Content.Should().Contain("without returning response text");
        assistant.Contents.Should().ContainSingle();
        assistant.Contents[0].Text.Should().Be(assistant.Content);
        context.IncidentReporter.Reports.Should().ContainSingle();

        var persisted = await context.Store.GetMessagesAsync(
            context.ViewModel.SelectedConversation!.Id,
            null,
            10,
            CancellationToken.None);
        persisted[1].State.Should().Be(DirectChatMessageState.Failed);
        persisted[1].Content.Should().Be(assistant.Content);
    }

    private static async Task<ViewModelContext> CreateViewModelAsync(ILlmClientService provider)
    {
        var directory = Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));
        var store = new SqliteDirectChatStore(Path.Combine(directory, "direct-chat.db"));
        var options = Options.Create(new DirectChatOptions());
        var estimator = new ConservativeTokenEstimator(options);
        var aiOptions = new MutableOptionsMonitor<AIOptions>(new AIOptions
        {
            GenerationDefaults = new AIGenerationDefaults { ContextWindowTokens = 8192 },
        });
        var factory = new Mock<ILlmClientFactory>();
        factory.Setup(static item => item.Create()).Returns(provider);
        var webView = new Mock<IWebView2AvailabilityService>();
        webView.Setup(static item => item.IsAvailable()).Returns(false);
        var reporter = new StubIncidentReporter();
        var viewModel = new DirectChatViewModel(
            store,
            new DirectChatContextBuilder(estimator, aiOptions, options),
            Mock.Of<IDirectChatHistoryCompressor>(),
            estimator,
            factory.Object,
            Mock.Of<IMyLocalAgentService>(),
            Mock.Of<ITaskStore>(),
            WeakReferenceMessenger.Default,
            aiOptions,
            reporter,
            Mock.Of<IDesktopInteractionService>(),
            new MarkdownRenderingService(),
            webView.Object,
            new DirectChatMediaFileService());
        await viewModel.LoadCommand.ExecuteAsync(null);
        return new ViewModelContext(directory, store, viewModel, reporter);
    }

    private sealed class ViewModelContext(
        string directory,
        SqliteDirectChatStore store,
        DirectChatViewModel viewModel,
        StubIncidentReporter incidentReporter) : IAsyncDisposable
    {
        public SqliteDirectChatStore Store { get; } = store;
        public DirectChatViewModel ViewModel { get; } = viewModel;
        public StubIncidentReporter IncidentReporter { get; } = incidentReporter;

        public ValueTask DisposeAsync()
        {
            Store.Dispose();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
