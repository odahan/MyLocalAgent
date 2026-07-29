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
        registry.Setup(instance => instance.GetAgentTools(It.IsAny<IEnumerable<string>>()))
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
        registry.Setup(instance => instance.GetAgentTools(It.IsAny<IEnumerable<string>>()))
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
        registry.Setup(instance => instance.GetAgentTools(It.IsAny<IEnumerable<string>>()))
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
}
