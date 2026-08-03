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

    /// <summary>
    /// Verifies Runner resolves task-owned prompt values and carries content-free warnings.
    /// </summary>
    [Fact]
    public async Task PromptOnlyTaskResolvesBeforeAgentAndCarriesSafeWarnings()
    {
        const string canaryMarker = "{{Private_Customer_Canary}}";
        const string canaryTaskName = "Private task name canary";
        var task = new ScheduledTask
        {
            Id = "runner-placeholders",
            Name = canaryTaskName,
            Schedule = "0 7 * * *",
            Prompt =
                $"Write Sort-{{{{Today[yyyyMMdd]}}}}.md for {{{{task.id}}}} and {canaryMarker} plus {{{{previous.output}}}}.",
            Tools = [ToolGroups.FileSystem],
            FileSystemAccess = [],
            Workflow = [],
            Enabled = true,
            ErrorPolicy = WorkflowErrorPolicy.StopOnError,
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    task.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        agent.Setup(
                instance => instance.RunPromptAsync(
                    $"Write Sort-20260803.md for {task.Id} and {canaryMarker} plus {{{{previous.output}}}}.",
                    It.Is<IEnumerable<string>>(
                        tools => tools.SequenceEqual(task.Tools)),
                    It.Is<ToolExecutionContext>(
                        context => context.TaskId == task.Id),
                    It.IsAny<EffectiveGenerationPolicy>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult("done", "Test"));
        WorkflowResult? capturedResult = null;
        var reportWriter = new Mock<IReportWriter>(MockBehavior.Strict);
        reportWriter.Setup(
                instance => instance.WriteAsync(
                    task,
                    It.IsAny<WorkflowResult>(),
                    "Test",
                    It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, WorkflowResult, string, CancellationToken>(
                (_, result, _, _) => capturedResult = result)
            .ReturnsAsync(new ReportWriteResult("report.md", "# Report"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AI:Provider"] = AIOptions.OllamaProvider,
                })
            .Build();
        var localNow = new DateTimeOffset(
            2026,
            8,
            3,
            14,
            30,
            25,
            TimeSpan.FromHours(2));
        var services = new ServiceCollection()
            .AddSingleton<ITaskStore>(store.Object)
            .AddSingleton<IMyLocalAgentService>(agent.Object)
            .AddSingleton<IReportWriter>(reportWriter.Object)
            .AddSingleton<TimeProvider>(new FixedLocalTimeProvider(localNow))
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<Microsoft.Extensions.Options.IOptionsMonitor<AIOptions>>(
                new MutableOptionsMonitor<AIOptions>(new AIOptions()))
            .BuildServiceProvider();
        var output = new StringWriter();

        var exitCode = await RunnerCommandHandlers.TestPromptAsync(
            services,
            task.Id,
            output,
            new StringWriter(),
            CancellationToken.None);

        exitCode.Should().Be(RunnerExitCodes.Success);
        capturedResult.Should().NotBeNull();
        capturedResult!.Success.Should().BeTrue();
        capturedResult.Warnings.Should().ContainSingle();
        capturedResult.Warnings[0].OccurrenceCount.Should().Be(2);
        capturedResult.Warnings[0].Message.Should().Contain("1 unknown");
        capturedResult.Warnings[0].Message.Should().Contain("1 workflow-only");
        output.ToString().Should().Contain("2 agent prompt placeholder occurrences");
        output.ToString().Should().NotContain(canaryMarker);
        output.ToString().Should().NotContain(canaryTaskName);
        output.ToString().Should().NotContain("20260803");
        agent.VerifyAll();
        reportWriter.VerifyAll();
    }

    private sealed class FixedLocalTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedLocalTimeProvider(DateTimeOffset localNow)
        {
            _utcNow = localNow.ToUniversalTime();
            _localTimeZone = TimeZoneInfo.CreateCustomTimeZone(
                "Runner placeholder test",
                localNow.Offset,
                "Runner placeholder test",
                "Runner placeholder test");
        }

        public override TimeZoneInfo LocalTimeZone => _localTimeZone;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
