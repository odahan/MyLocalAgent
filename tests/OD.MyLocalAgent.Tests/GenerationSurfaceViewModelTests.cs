using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using OllamaSharp.Models;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies generation-policy propagation from interactive user surfaces.
/// </summary>
public sealed class GenerationSurfaceViewModelTests
{
    /// <summary>
    /// Verifies Prompt editor Test uses the unsaved task override snapshot supplied by Task editor.
    /// </summary>
    [Fact]
    public async Task PromptTestUsesUnsavedTaskGenerationSnapshot()
    {
        var storedTask = new ScheduledTask
        {
            Id = "prompt-generation",
            Name = "Prompt generation",
            Schedule = "0 7 * * *",
            Prompt = "Stored prompt",
            Tools = [ToolGroups.FileSystem],
            FileSystemAccess = [],
            Workflow = [],
            Enabled = true,
            ErrorPolicy = WorkflowErrorPolicy.StopOnError,
            GenerationOptions = new GenerationOptions
            {
                Temperature = 0.8,
            },
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    storedTask.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedTask);
        EffectiveGenerationPolicy? capturedPolicy = null;
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        agent.Setup(
                instance => instance.RunPromptStreamingAsync(
                    It.IsAny<string>(),
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<ToolExecutionContext>(),
                    It.IsAny<EffectiveGenerationPolicy>(),
                    It.IsAny<CancellationToken>()))
            .Returns(
                (
                    string _,
                    IEnumerable<string> _,
                    ToolExecutionContext _,
                    EffectiveGenerationPolicy policy,
                    CancellationToken cancellationToken) =>
                {
                    capturedPolicy = policy;
                    return StreamAsync("done", cancellationToken);
                });
        var options = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults
                {
                    Temperature = 0.2,
                    TopP = 0.91,
                    ContextWindowTokens = 16_384,
                },
            });
        var viewModel = new PromptEditorViewModel(
            store.Object,
            Mock.Of<ITaskSchedulerService>(),
            agent.Object,
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IMessenger>(),
            options);

        viewModel.Open(
            storedTask.Id,
            new GenerationOptions
            {
                Temperature = 0.45,
            },
            useGenerationOptionsSnapshot: true);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.Prompt = "Unsaved prompt text";

        await viewModel.TestPromptCommand.ExecuteAsync(null);

        capturedPolicy.Should().NotBeNull();
        capturedPolicy!.Temperature.Should().Be(0.45);
        capturedPolicy.TemperatureSource.Should().Be(GenerationOptionSource.Task);
        capturedPolicy.TopP.Should().Be(0.91);
        capturedPolicy.TopPSource.Should().Be(GenerationOptionSource.Settings);
        capturedPolicy.ContextWindowTokens.Should().Be(16_384);
        viewModel.GenerationPolicyText.Should().Contain("Task");
        viewModel.GenerationPolicyText.Should().Contain("Settings");
        store.VerifyAll();
        agent.VerifyAll();
    }

    /// <summary>
    /// Verifies Prompt editor expands current unsaved text with loaded identity and safe warnings.
    /// </summary>
    [Fact]
    public async Task PromptTestResolvesUnsavedTextAndKeepsWarningsSeparateFromOutput()
    {
        const string unknown = "{{Customer_Canary}}";
        var storedTask = new ScheduledTask
        {
            Id = "prompt-placeholders",
            Name = "Résumé task",
            Schedule = "0 7 * * *",
            Prompt = "Stored prompt",
            Tools = [ToolGroups.FileSystem],
            FileSystemAccess = [],
            Workflow = [],
            Enabled = true,
            ErrorPolicy = WorkflowErrorPolicy.StopOnError,
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    storedTask.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedTask);
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        agent.Setup(
                instance => instance.RunPromptStreamingAsync(
                    $"Unsaved 20260803 {storedTask.Id} {storedTask.Name} {unknown}",
                    It.IsAny<IEnumerable<string>>(),
                    It.Is<ToolExecutionContext>(
                        context => context.TaskId == storedTask.Id),
                    It.IsAny<EffectiveGenerationPolicy>(),
                    It.IsAny<CancellationToken>()))
            .Returns(
                (string _, IEnumerable<string> _, ToolExecutionContext _, EffectiveGenerationPolicy _, CancellationToken cancellationToken) =>
                    StreamAsync("model output", cancellationToken));
        var localNow = new DateTimeOffset(
            2026,
            8,
            3,
            14,
            30,
            25,
            TimeSpan.FromHours(2));
        var viewModel = new PromptEditorViewModel(
            store.Object,
            Mock.Of<ITaskSchedulerService>(),
            agent.Object,
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IMessenger>(),
            new MutableOptionsMonitor<AIOptions>(new AIOptions()),
            new FixedLocalTimeProvider(localNow));

        viewModel.Open(storedTask.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.Prompt =
            $"Unsaved {{{{Today[yyyyMMdd]}}}} {{{{task.id}}}} {{{{task.name}}}} {unknown}";

        await viewModel.TestPromptCommand.ExecuteAsync(null);

        viewModel.Output.Should().Be("model output");
        viewModel.ChunkCount.Should().Be(1);
        viewModel.HasPlaceholderWarnings.Should().BeTrue();
        viewModel.PlaceholderWarningText.Should().Contain("1 unknown");
        viewModel.PlaceholderWarningText.Should().NotContain(unknown);
        viewModel.StatusText.Should().Contain("remained unresolved");

        viewModel.Prompt += " edited";

        viewModel.HasPlaceholderWarnings.Should().BeFalse();
        viewModel.PlaceholderWarningText.Should().BeEmpty();
        agent.VerifyAll();
    }

    /// <summary>
    /// Verifies Prompt editor Save persists the entered placeholder template without trimming it.
    /// </summary>
    [Fact]
    public async Task PromptSavePreservesOriginalTemplateExactly()
    {
        var storedTask = new ScheduledTask
        {
            Id = "prompt-save-template",
            Name = "Prompt save template",
            Schedule = "0 7 * * *",
            Prompt = "Stored prompt",
            Tools = [],
            FileSystemAccess = [],
            Workflow = [],
            Enabled = true,
            ErrorPolicy = WorkflowErrorPolicy.StopOnError,
        };
        const string unsavedTemplate = "  Create Sort-{{Today[yyyyMMdd]}}.md.\r\n  ";
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    storedTask.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedTask);
        store.Setup(
                instance => instance.SaveAsync(
                    It.Is<ScheduledTask>(task => task.Prompt == unsavedTemplate),
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var scheduler = new Mock<ITaskSchedulerService>(MockBehavior.Strict);
        scheduler.Setup(
                instance => instance.RegisterAsync(
                    It.Is<ScheduledTask>(task => task.Prompt == unsavedTemplate),
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var viewModel = new PromptEditorViewModel(
            store.Object,
            scheduler.Object,
            Mock.Of<IMyLocalAgentService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IMessenger>());

        viewModel.Open(storedTask.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.Prompt = unsavedTemplate;

        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.Prompt.Should().Be(unsavedTemplate);
        store.VerifyAll();
        scheduler.VerifyAll();
    }

    /// <summary>
    /// Verifies the AI Playground uses the current global policy in raw-provider mode.
    /// </summary>
    [Fact]
    public async Task PlaygroundUsesGlobalGenerationDefaults()
    {
        var provider = new StubLlmClientService(
            new StubChatClient("playground result"),
            AIOptions.OllamaProvider)
        {
            ModelName = "test-model",
        };
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var aiOptions = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                Provider = AIOptions.OllamaProvider,
                GenerationDefaults = new AIGenerationDefaults
                {
                    Temperature = 0.65,
                    TopP = 0.72,
                    ContextWindowTokens = 24_000,
                },
            });
        var viewModel = new AIPlaygroundViewModel(
            factory.Object,
            Mock.Of<IMyLocalAgentService>(),
            aiOptions,
            new MutableOptionsMonitor<OllamaOptions>(
                new OllamaOptions
                {
                    Model = "test-model",
                }),
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions()),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IMessenger>())
        {
            Prompt = "Test {{ModelInfo}}.",
        };

        await viewModel.GenerateCommand.ExecuteAsync(null);

        provider.Client.LastOptions.Should().NotBeNull();
        provider.Client.LastOptions!.Temperature.Should().Be(0.65f);
        provider.Client.LastOptions.TopP.Should().Be(0.72f);
        provider.Client.LastOptions.AdditionalProperties.Should()
            .ContainKey(OllamaOption.NumCtx.Name);
        viewModel.GenerationPolicyText.Should().Contain("24");
        viewModel.Output.Should().Be("playground result");
        provider.Client.LastMessages.Should().ContainSingle();
        provider.Client.LastMessages[0].Text.Should().Be(
            "Test ModelName test-model - Temp 0.65 - TopP 0.72 - Context 24000.");
        factory.VerifyAll();
    }

    /// <summary>
    /// Verifies invalid Settings input exposes validation and disables Save.
    /// </summary>
    [Fact]
    public void SettingsInvalidInputDisablesSave()
    {
        var viewModel = new SettingsViewModel(
            new MutableOptionsMonitor<AIOptions>(new AIOptions()),
            new MutableOptionsMonitor<OllamaOptions>(new OllamaOptions()),
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions()),
            new MutableOptionsMonitor<FileSystemOptions>(
                new FileSystemOptions()),
            new MutableOptionsMonitor<HttpOptions>(new HttpOptions()),
            new MutableOptionsMonitor<SchedulerOptions>(
                new SchedulerOptions()),
            new MutableOptionsMonitor<GuardrailsOptions>(
                new GuardrailsOptions()),
            new MutableOptionsMonitor<LoggingOptions>(new LoggingOptions()),
            Mock.Of<ILlmClientFactory>(),
            Mock.Of<IOllamaModelCatalog>(),
            Mock.Of<ISecretStore>(),
            Mock.Of<IUserSettingsService>(),
            Mock.Of<IThemeService>(),
            Mock.Of<IIncidentLogService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IInMemoryLogSink>(),
            Mock.Of<ILogManagementService>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System);

        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();

        viewModel.GenerationTemperatureText = "NaN";

        viewModel.HasGenerationValidationError.Should().BeTrue();
        viewModel.GenerationValidationText.Should().Contain("Temperature");
        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();

        viewModel.GenerationTemperatureText = "0.4";
        viewModel.GenerationTopPText = "0.8";
        viewModel.ContextWindowTokensText = "16384";

        viewModel.HasGenerationValidationError.Should().BeFalse();
        viewModel.SaveCommand.CanExecute(null).Should().BeTrue();

        viewModel.SqliteDefaultMaxRows = 0;

        viewModel.HasSqliteValidationError.Should().BeTrue();
        viewModel.SqliteValidationText.Should().Contain("Default maximum rows");
        viewModel.SaveCommand.CanExecute(null).Should().BeFalse();

        viewModel.SqliteDefaultMaxRows = 100;

        viewModel.HasSqliteValidationError.Should().BeFalse();
        viewModel.SaveCommand.CanExecute(null).Should().BeTrue();
    }

    private static async IAsyncEnumerable<string> StreamAsync(
        string value,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return value;
    }

    private sealed class FixedLocalTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedLocalTimeProvider(DateTimeOffset localNow)
        {
            _utcNow = localNow.ToUniversalTime();
            _localTimeZone = TimeZoneInfo.CreateCustomTimeZone(
                "Prompt editor placeholder test",
                localNow.Offset,
                "Prompt editor placeholder test",
                "Prompt editor placeholder test");
        }

        public override TimeZoneInfo LocalTimeZone => _localTimeZone;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
