using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies agent provider failures and successful framework execution.
/// </summary>
public sealed class MyLocalAgentServiceTests
{
    /// <summary>
    /// Verifies factory configuration failures are reported and surfaced with a friendly message.
    /// </summary>
    [Fact]
    public async Task FactoryFailureReportsIncidentAndThrowsFriendlyError()
    {
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create())
            .Throws(new LlmConfigurationException("Provider is invalid."));
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        registry.Setup(
                instance => instance.GetAgentTools(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<ToolExecutionContext>()))
            .Returns([]);
        var reporter = new Mock<IIncidentReporter>(MockBehavior.Strict);
        reporter.Setup(instance => instance.ReportAsync(
                It.IsAny<LlmConfigurationException>(),
                nameof(MyLocalAgentService),
                IncidentSeverity.Error,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("incident-123");
        var service = new MyLocalAgentService(
            factory.Object,
            registry.Object,
            [],
            reporter.Object,
            TimeProvider.System);

        var action = () => service.RunPromptAsync(
            "Do the task.",
            [],
            CancellationToken.None);

        await action.Should().ThrowAsync<AgentExecutionException>()
            .Where(exception =>
                exception.CorrelationId == "incident-123" &&
                exception.Message.Contains("Review Settings", StringComparison.Ordinal));
        reporter.VerifyAll();
    }

    /// <summary>
    /// Verifies a stub chat client runs through the Agent Framework happy path.
    /// </summary>
    [Fact]
    public async Task HappyPathReturnsAgentResponseAndProviderName()
    {
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create())
            .Returns(new StubLlmClientService("Concise report."));
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        registry.Setup(
                instance => instance.GetAgentTools(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<ToolExecutionContext>()))
            .Returns([]);
        var reporter = new Mock<IIncidentReporter>(MockBehavior.Strict);
        var service = new MyLocalAgentService(
            factory.Object,
            registry.Object,
            [],
            reporter.Object,
            TimeProvider.System);

        var result = await service.RunPromptAsync(
            "Do the task.",
            [],
            CancellationToken.None);

        result.Text.Should().Be("Concise report.");
        result.ProviderName.Should().Be("Stub");
    }

    /// <summary>
    /// Verifies built-in skills are exposed only for the interactive opt-in overload.
    /// </summary>
    [Fact]
    public async Task InteractiveSkillOptInAddsSkillFunctionToAgent()
    {
        var provider = new StubLlmClientService("Concise report.");
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        registry.Setup(
                instance => instance.GetAgentTools(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<ToolExecutionContext>()))
            .Returns([]);
        var reporter = new Mock<IIncidentReporter>(MockBehavior.Strict);
        var service = new MyLocalAgentService(
            factory.Object,
            registry.Object,
            [new SummarizeTextSkill(factory.Object)],
            reporter.Object,
            TimeProvider.System);

        _ = await service.RunPromptAsync(
            "Summarize this content.",
            [],
            allowSkills: true,
            CancellationToken.None);

        provider.Client.LastOptions.Should().NotBeNull();
        provider.Client.LastOptions!.Tools.Should().ContainSingle(
            tool => tool.Name == "skill_summarize");
    }

    /// <summary>
    /// Verifies a task execution context is used when agent tools are created.
    /// </summary>
    [Fact]
    public async Task TaskContextIsPassedToAgentToolCreation()
    {
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create())
            .Returns(new StubLlmClientService("Done."));
        var authorizedPath = Path.Combine(Path.GetTempPath(), "agent-authorized");
        var context = new ToolExecutionContext(
            "agent-task",
            [
                new FileSystemAccessRule
                {
                    Path = authorizedPath,
                    Access = FileSystemAccessMode.ReadOnly,
                },
            ]);
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        registry.Setup(
                instance => instance.GetAgentTools(
                    It.IsAny<IEnumerable<string>>(),
                    It.Is<ToolExecutionContext>(
                        value =>
                            value.TaskId == "agent-task" &&
                            value.FileSystemAccess.Count == 1)))
            .Returns([]);
        var service = new MyLocalAgentService(
            factory.Object,
            registry.Object,
            [],
            Mock.Of<IIncidentReporter>(),
            TimeProvider.System);

        var result = await service.RunPromptAsync(
            "Do the task.",
            [ToolGroups.FileSystem],
            context,
            CancellationToken.None);

        result.Text.Should().Be("Done.");
        registry.VerifyAll();
    }

    /// <summary>
    /// Verifies one immutable policy is applied to the agent request without losing tools.
    /// </summary>
    [Fact]
    public async Task ExplicitGenerationPolicyReachesAgentRequest()
    {
        var provider = new StubLlmClientService(
            new StubChatClient("Done."),
            AIOptions.OllamaProvider);
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        registry.Setup(
                instance => instance.GetAgentTools(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<ToolExecutionContext>()))
            .Returns([]);
        var service = new MyLocalAgentService(
            factory.Object,
            registry.Object,
            [],
            Mock.Of<IIncidentReporter>(),
            TimeProvider.System);
        var policy = new EffectiveGenerationPolicy(
            0.45,
            0.75,
            24_000,
            GenerationOptionSource.Task,
            GenerationOptionSource.Step);

        _ = await service.RunPromptAsync(
            "Do the task.",
            [],
            ToolExecutionContext.Taskless,
            policy,
            CancellationToken.None);

        provider.Client.LastOptions.Should().NotBeNull();
        provider.Client.LastOptions!.Temperature.Should().Be(0.45f);
        provider.Client.LastOptions.TopP.Should().Be(0.75f);
        provider.Client.LastOptions.Tools.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies taskless calls resolve reloadable global defaults at the start of each execution.
    /// </summary>
    [Fact]
    public async Task NewExecutionObservesReloadedGlobalDefaults()
    {
        var provider = new StubLlmClientService(
            new StubChatClient(["first", "second"]),
            AIOptions.OpenAIProvider);
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var registry = new Mock<IToolRegistry>(MockBehavior.Strict);
        registry.Setup(
                instance => instance.GetAgentTools(
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<ToolExecutionContext>()))
            .Returns([]);
        var options = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults
                {
                    Temperature = 0.2,
                    TopP = 1,
                    ContextWindowTokens = 8192,
                },
            });
        var service = new MyLocalAgentService(
            factory.Object,
            registry.Object,
            [],
            Mock.Of<IIncidentReporter>(),
            TimeProvider.System,
            options);

        _ = await service.RunPromptAsync(
            "First.",
            [],
            CancellationToken.None);
        options.Set(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults
                {
                    Temperature = 0.9,
                    TopP = 0.7,
                    ContextWindowTokens = 32_000,
                },
            });
        _ = await service.RunPromptAsync(
            "Second.",
            [],
            CancellationToken.None);

        provider.Client.OptionCalls.Should().HaveCount(2);
        provider.Client.OptionCalls[0]!.Temperature.Should().Be(0.2f);
        provider.Client.OptionCalls[1]!.Temperature.Should().Be(0.9f);
        provider.Client.OptionCalls[1]!.TopP.Should().Be(0.7f);
    }
}
