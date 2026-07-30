using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the tool-less assistant boundary, repair state machine, cancellation, and safe rendering.
/// </summary>
public sealed class WorkflowAssistantServiceTests
{
    /// <summary>
    /// Verifies generation context comes from the current Tool Map and shared placeholder catalog.
    /// </summary>
    [Fact]
    public void GenerationPromptContainsDynamicCatalogs()
    {
        var prompt = WorkflowAssistantPromptBuilder.BuildGenerationSystemPrompt();

        foreach (var definition in ToolMap.Definitions)
        {
            prompt.Should().Contain(definition.Alias);
        }

        foreach (var placeholder in WorkflowPlaceholderCatalog.Definitions)
        {
            prompt.Should().Contain(placeholder);
        }

        prompt.Should().Contain(WorkflowAssistantPromptBuilder.GenerationVersion);
    }

    /// <summary>
    /// Verifies the current factory result receives one system and one user message with no tools.
    /// </summary>
    [Fact]
    public async Task ServiceUsesCurrentFactoryStreamingAndNoTools()
    {
        var chat = new StubChatClient("""["gmail.readToday"]""");
        var provider = new StubLlmClientService(chat, "Current");
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var service = new WorkflowAssistantService(factory.Object);

        var chunks = new List<string>();
        await foreach (var chunk in service.StreamGenerationAsync(
            "Read today's mail.",
            CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        string.Concat(chunks).Should().Be("""["gmail.readToday"]""");
        chat.CallCount.Should().Be(1);
        chat.LastMessages.Should().HaveCount(2);
        chat.LastMessages[0].Role.Should().Be(ChatRole.System);
        chat.LastMessages[1].Role.Should().Be(ChatRole.User);
        chat.LastOptions.Should().NotBeNull();
        chat.LastOptions!.Tools.Should().BeNull();
        factory.VerifyAll();
    }

    /// <summary>
    /// Verifies deterministic invalid output receives exactly two repairs and retains the last error.
    /// </summary>
    [Fact]
    public async Task ViewModelStopsAfterThreeDeterministicAttempts()
    {
        var chat = new StubChatClient(["not json", "```json\n[]\n```", "{}"]);
        var viewModel = CreateViewModel(chat, out var incidents);
        viewModel.Initialize(CreateRequest());
        viewModel.Description = "Read today's mail.";

        await viewModel.GenerateCommand.ExecuteAsync(null);

        chat.CallCount.Should().Be(3);
        viewModel.State.Should().Be(AIWorkflowHelperState.Failed);
        viewModel.ResponseText.Should().Be("{}");
        viewModel.ValidationErrors.Should().NotBeEmpty();
        chat.MessageCalls[1][1].Text.Should().Contain("Exact deterministic errors");
        chat.MessageCalls[2][1].Text.Should().Contain("complete replacement response");
        incidents.Reports.Should().BeEmpty();
        viewModel.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    /// <summary>
    /// Verifies a valid candidate requires explicit missing-group approval before Apply.
    /// </summary>
    [Fact]
    public async Task ProposalRefusalPreservesCandidateAndAcceptanceEnablesApply()
    {
        var chat = new StubChatClient("""["gmail.readToday"]""");
        var viewModel = CreateViewModel(chat, out _);
        viewModel.Initialize(CreateRequest());
        viewModel.Description = "Read today's mail.";

        await viewModel.GenerateCommand.ExecuteAsync(null);

        viewModel.State.Should().Be(AIWorkflowHelperState.NeedsApproval);
        viewModel.Proposals.Should().ContainSingle();
        viewModel.ApplyCommand.CanExecute(null).Should().BeFalse();
        viewModel.ResponseText.Should().Be("""["gmail.readToday"]""");

        viewModel.Proposals.Single().IsAccepted = true;
        viewModel.State.Should().Be(AIWorkflowHelperState.Ready);
        viewModel.ApplyCommand.CanExecute(null).Should().BeTrue();

        viewModel.Proposals.Single().IsAccepted = false;
        viewModel.State.Should().Be(AIWorkflowHelperState.NeedsApproval);
        viewModel.ResponseText.Should().Be("""["gmail.readToday"]""");

        viewModel.Proposals.Single().IsAccepted = true;
        viewModel.ApplyCommand.Execute(null);
        viewModel.Result.Should().NotBeNull();
        viewModel.Result!.ToolGroups.Should().Contain(ToolGroups.Gmail);
    }

    /// <summary>
    /// Verifies one-use refinement rebuilds the deterministic description and clears the instruction.
    /// </summary>
    [Fact]
    public async Task RefinementUsesCurrentCandidateAndClearsSingleUseInstruction()
    {
        var chat = new StubChatClient("""["gmail.readUnread"]""");
        var viewModel = CreateViewModel(chat, out _);
        var current = WorkflowDefinitionSerializer.Deserialize("""["gmail.readToday"]""");
        viewModel.Initialize(
            CreateRequest() with
            {
                Workflow = current,
                ToolGroups = [ToolGroups.Gmail],
            });
        viewModel.Refinement = "Read only unread messages.";

        await viewModel.RefineCommand.ExecuteAsync(null);

        chat.CallCount.Should().Be(1);
        chat.LastMessages[1].Text.Should().Contain("Current canonical workflow")
            .And.Contain("Read only unread messages.");
        viewModel.Refinement.Should().BeEmpty();
        viewModel.Description.Should().Contain("Alias: gmail.readUnread");
        viewModel.State.Should().Be(AIWorkflowHelperState.Ready);
    }

    /// <summary>
    /// Verifies Explain produces Text and restricted HTML from one streamed response.
    /// </summary>
    [Fact]
    public async Task ExplainUsesOneResponseForTextAndRenderedMarkdown()
    {
        var chat = new StubChatClient("# Purpose\nReads today's mail.");
        var viewModel = CreateViewModel(chat, out _);
        viewModel.Initialize(
            CreateRequest() with
            {
                Mode = AIWorkflowHelperMode.Explain,
                Workflow = WorkflowDefinitionSerializer.Deserialize("""["gmail.readToday"]"""),
                ToolGroups = [ToolGroups.Gmail],
            });

        await viewModel.GenerateCommand.ExecuteAsync(null);

        chat.CallCount.Should().Be(1);
        viewModel.State.Should().Be(AIWorkflowHelperState.Ready);
        viewModel.PlainTextExplanation.Should().Contain("Purpose")
            .And.Contain("Reads today's mail.");
        viewModel.RenderedHtml.Should().Contain("<h1")
            .And.Contain("default-src 'none'");
        chat.LastMessages[1].Text.Should().Contain("Canonical workflow")
            .And.Contain("Deterministic application analysis");
    }

    /// <summary>
    /// Verifies cancellation preserves partial output, performs no repair, and reports no incident.
    /// </summary>
    [Fact]
    public async Task CancellationPreservesPartialOutputWithoutRepairOrIncident()
    {
        var assistant = new CancellableAssistant();
        var incidents = new StubIncidentReporter();
        var viewModel = new AIWorkflowHelperViewModel(
            assistant,
            new MarkdownRenderingService(),
            incidents,
            Mock.Of<IDesktopInteractionService>());
        viewModel.Initialize(CreateRequest());
        viewModel.Description = "Create a workflow.";

        var operation = viewModel.GenerateCommand.ExecuteAsync(null);
        await assistant.Started.Task;
        viewModel.CancelCommand.Execute(null);
        await operation;

        assistant.CallCount.Should().Be(1);
        viewModel.State.Should().Be(AIWorkflowHelperState.Cancelled);
        viewModel.ResponseText.Should().Be("[");
        viewModel.ApplyCommand.CanExecute(null).Should().BeFalse();
        incidents.Reports.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies one Markdown response produces plain text and a remote-resource-blocking document.
    /// </summary>
    [Fact]
    public void MarkdownRendererDisablesRawHtmlAndRemoteResources()
    {
        var renderer = new MarkdownRenderingService();
        const string markdown = "# Title\n<script>alert(1)</script>\n[remote](https://example.com)";

        var text = renderer.ToPlainText(markdown);
        var html = renderer.ToRestrictedHtml(markdown);

        text.Should().Contain("Title");
        html.Should().Contain("default-src 'none'")
            .And.Contain("script-src 'none'")
            .And.NotContain("<script>alert(1)</script>");
    }

    /// <summary>
    /// Verifies a missing runtime keeps Text available and exposes only the official explicit link.
    /// </summary>
    [Fact]
    public void MissingWebView2RuntimeUsesTextFallbackAndOfficialLink()
    {
        var availability = new Mock<IWebView2AvailabilityService>(MockBehavior.Strict);
        availability.Setup(instance => instance.IsAvailable()).Returns(false);
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(
                instance => instance.OpenUri(
                    "https://developer.microsoft.com/microsoft-edge/webview2/"));
        var viewModel = new AIWorkflowHelperViewModel(
            Mock.Of<IWorkflowAssistantService>(),
            new MarkdownRenderingService(),
            new StubIncidentReporter(),
            desktop.Object,
            availability.Object);

        viewModel.IsRenderedAvailable.Should().BeFalse();
        viewModel.IsRenderedUnavailable.Should().BeTrue();
        viewModel.ShowRenderedMarkdown.Should().BeFalse();

        viewModel.OpenWebView2DownloadPageCommand.Execute(null);

        availability.VerifyAll();
        desktop.VerifyAll();
    }

    private static AIWorkflowHelperViewModel CreateViewModel(
        StubChatClient chat,
        out StubIncidentReporter incidents)
    {
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create())
            .Returns(new StubLlmClientService(chat, "Current"));
        incidents = new StubIncidentReporter();
        return new AIWorkflowHelperViewModel(
            new WorkflowAssistantService(factory.Object),
            new MarkdownRenderingService(),
            incidents,
            Mock.Of<IDesktopInteractionService>());
    }

    private static AIWorkflowHelperRequest CreateRequest() =>
        new(
            AIWorkflowHelperMode.Author,
            [],
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            WorkflowErrorPolicy.StopOnError,
            [],
            [],
            new SafetyOptions());

    private sealed class CancellableAssistant : IWorkflowAssistantService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public async IAsyncEnumerable<string> StreamGenerationAsync(
            string userMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CallCount++;
            yield return "[";
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public async IAsyncEnumerable<string> StreamExplanationAsync(
            string userMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }
}
