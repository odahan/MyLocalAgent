using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Runner;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.Workflows;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies Runner task boundaries resolve and propagate generation policies.
/// </summary>
public sealed class RunnerGenerationPolicyTests
{
    /// <summary>
    /// Verifies a prompt-only Runner execution receives the task-effective policy.
    /// </summary>
    [Fact]
    public async Task PromptOnlyTaskReceivesTaskEffectiveGenerationPolicy()
    {
        var task = new ScheduledTask
        {
            Id = $"runner-generation-{Guid.NewGuid():N}",
            Name = "Runner generation",
            Schedule = "0 7 * * *",
            Prompt = "Run the prompt.",
            Tools = [ToolGroups.FileSystem],
            FileSystemAccess = [],
            Workflow = [],
            Enabled = true,
            ErrorPolicy = WorkflowErrorPolicy.StopOnError,
            GenerationOptions = new GenerationOptions
            {
                Temperature = 0.35,
            },
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    task.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        EffectiveGenerationPolicy? capturedPolicy = null;
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        agent.Setup(
                instance => instance.RunPromptAsync(
                    task.Prompt,
                    It.Is<IEnumerable<string>>(
                        tools => tools.SequenceEqual(task.Tools)),
                    It.Is<ToolExecutionContext>(
                        context => context.TaskId == task.Id),
                    It.IsAny<EffectiveGenerationPolicy>(),
                    It.IsAny<CancellationToken>()))
            .Callback<
                string,
                IEnumerable<string>,
                ToolExecutionContext,
                EffectiveGenerationPolicy,
                CancellationToken>(
                (_, _, _, policy, _) => capturedPolicy = policy)
            .ReturnsAsync(new AgentRunResult("done", "Test"));
        var reportWriter = new Mock<IReportWriter>(MockBehavior.Strict);
        reportWriter.Setup(
                instance => instance.WriteAsync(
                    task,
                    It.Is<WorkflowResult>(result => result.Success),
                    "Test",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportWriteResult("report.md", "# Report"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AI:Provider"] = AIOptions.OllamaProvider,
                })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<ITaskStore>(store.Object)
            .AddSingleton<IMyLocalAgentService>(agent.Object)
            .AddSingleton<IReportWriter>(reportWriter.Object)
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<Microsoft.Extensions.Options.IOptionsMonitor<AIOptions>>(
                new MutableOptionsMonitor<AIOptions>(
                    new AIOptions
                    {
                        GenerationDefaults = new AIGenerationDefaults
                        {
                            Temperature = 0.2,
                            TopP = 0.88,
                            ContextWindowTokens = 32_000,
                        },
                    }))
            .BuildServiceProvider();

        var exitCode = await RunnerCommandHandlers.TestPromptAsync(
            services,
            task.Id,
            new StringWriter(),
            new StringWriter(),
            CancellationToken.None);

        exitCode.Should().Be(RunnerExitCodes.Success);
        capturedPolicy.Should().NotBeNull();
        capturedPolicy!.Temperature.Should().Be(0.35);
        capturedPolicy.TemperatureSource.Should().Be(GenerationOptionSource.Task);
        capturedPolicy.TopP.Should().Be(0.88);
        capturedPolicy.TopPSource.Should().Be(GenerationOptionSource.Settings);
        capturedPolicy.ContextWindowTokens.Should().Be(32_000);
        store.VerifyAll();
        agent.VerifyAll();
        reportWriter.VerifyAll();
    }
}
