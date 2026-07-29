using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Workflows;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies deterministic sequential workflow execution.
/// </summary>
public sealed class WorkflowEngineTests
{
    /// <summary>
    /// Verifies step order and every task, named-output, and previous-output placeholder.
    /// </summary>
    [Fact]
    public async Task ExecutesInOrderAndResolvesToolArgumentPlaceholders()
    {
        var registry = new RecordingToolRegistry(
            (alias, arguments, _) =>
            {
                if (alias == "filesystem.list")
                {
                    arguments.GetProperty("path").GetString().Should().Be("workflow-test");
                    return Task.FromResult("first-output");
                }

                arguments.GetProperty("path").GetString().Should().Be("Workflow test");
                arguments.GetProperty("content").GetString().Should().Be(
                    "first-output|first-output|original prompt");
                return Task.FromResult("second-output");
            });
        var engine = CreateEngine(registry);
        var task = CreateTask(
            [
                Step(
                    "filesystem.list",
                    "first",
                    """{ "path": "{{task.id}}" }"""),
                Step(
                    "filesystem.write",
                    "second",
                    """
                    {
                      "path": "{{task.name}}",
                      "content": "{{first.output}}|{{previous.output}}|{{task.prompt}}"
                    }
                    """),
            ]);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Steps.Select(static step => step.Id).Should().Equal("first", "second");
        registry.Aliases.Should().Equal("filesystem.list", "filesystem.write");
        result.FinalText.Should().Be("second-output");
    }

    /// <summary>
    /// Verifies an unknown placeholder fails the current step.
    /// </summary>
    [Fact]
    public async Task UnknownPlaceholderFailsStep()
    {
        var registry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("unused"));
        var engine = CreateEngine(registry);
        var task = CreateTask(
            [
                Step(
                    "filesystem.list",
                    "read",
                    """{ "path": "{{missing.output}}" }"""),
            ]);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Error.Should().BeOfType<WorkflowPlaceholderException>();
        registry.Aliases.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies string and object forms create equivalent executable step models.
    /// </summary>
    [Fact]
    public async Task StringAndObjectFormsExecuteEquivalently()
    {
        var stringTask = ScheduledTaskSerializer.Deserialize(
            CreateTaskJson("\"gmail.readToday\"", "gmail"));
        var objectTask = ScheduledTaskSerializer.Deserialize(
            CreateTaskJson("""{ "step": "gmail.readToday" }""", "gmail"));
        var stringRegistry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("""{"messages":[]}"""));
        var objectRegistry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("""{"messages":[]}"""));

        var stringStep = WorkflowStepFactory.Create(
            stringTask.Workflow[0],
            stringRegistry);
        var objectStep = WorkflowStepFactory.Create(
            objectTask.Workflow[0],
            objectRegistry);
        var stringResult = await CreateEngine(stringRegistry).ExecuteAsync(
            stringTask,
            CancellationToken.None);
        var objectResult = await CreateEngine(objectRegistry).ExecuteAsync(
            objectTask,
            CancellationToken.None);

        stringStep.Should().BeOfType<ToolStep>();
        objectStep.Should().BeOfType<ToolStep>();
        stringStep.Alias.Should().Be(objectStep.Alias);
        stringStep.Id.Should().Be(objectStep.Id);
        stringResult.FinalText.Should().Be(objectResult.FinalText);
    }

    /// <summary>
    /// Verifies StopOnError prevents all later steps from running.
    /// </summary>
    [Fact]
    public async Task StopOnErrorAbortsWorkflow()
    {
        var registry = new RecordingToolRegistry(
            (alias, _, _) => alias == "filesystem.list"
                ? Task.FromException<string>(new IOException("read failed"))
                : Task.FromResult("unexpected"));
        var engine = CreateEngine(registry);
        var task = CreateTask(
            [
                Step("filesystem.list", "first", """{ "path": "." }"""),
                Step(
                    "filesystem.createDirectory",
                    "second",
                    """{ "path": "later" }"""),
            ],
            WorkflowErrorPolicy.StopOnError);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        registry.Aliases.Should().Equal("filesystem.list");
    }

    /// <summary>
    /// Verifies ContinueOnError records failure and proceeds to later steps.
    /// </summary>
    [Fact]
    public async Task ContinueOnErrorRunsFollowingSteps()
    {
        var registry = new RecordingToolRegistry(
            (alias, _, _) => alias == "filesystem.list"
                ? Task.FromException<string>(new IOException("read failed"))
                : Task.FromResult("continued"));
        var engine = CreateEngine(registry);
        var task = CreateTask(
            [
                Step("filesystem.list", "first", """{ "path": "." }"""),
                Step(
                    "filesystem.createDirectory",
                    "second",
                    """{ "path": "later" }"""),
            ],
            WorkflowErrorPolicy.ContinueOnError);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Steps.Should().HaveCount(2);
        result.FinalText.Should().Be("continued");
        registry.Aliases.Should().Equal(
            "filesystem.list",
            "filesystem.createDirectory");
    }

    /// <summary>
    /// Verifies Retry performs the configured number of retries with exponential backoff.
    /// </summary>
    [Fact]
    public async Task RetryUsesExactCountAndExponentialBackoff()
    {
        var attempts = 0;
        var registry = new RecordingToolRegistry(
            (_, _, _) =>
            {
                attempts++;
                return attempts <= 3
                    ? Task.FromException<string>(new IOException("transient"))
                    : Task.FromResult("recovered");
            });
        var timeProvider = new RecordingTimeProvider();
        var engine = CreateEngine(registry, timeProvider: timeProvider);
        var task = CreateTask(
            [
                Step(
                    "filesystem.list",
                    "retrying",
                    """{ "path": "." }""",
                    WorkflowErrorPolicy.Retry,
                    retryCount: 3,
                    retryBackoffSeconds: 2),
            ]);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Steps[0].Attempts.Should().Be(4);
        timeProvider.Delays.Should().Equal(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8));
    }

    /// <summary>
    /// Verifies a declared workflow executes without invoking the plain prompt path.
    /// </summary>
    [Fact]
    public async Task WorkflowTakesPrecedenceOverPlainPrompt()
    {
        var registry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("workflow-output"));
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        var engine = CreateEngine(registry, agentService: agent.Object);
        var task = CreateTask(
            [Step("filesystem.list", "only", """{ "path": "." }""")]);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.FinalText.Should().Be("workflow-output");
        agent.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies registry whitelist enforcement is represented as a failed step.
    /// </summary>
    [Fact]
    public async Task ToolOutsideWhitelistFailsStep()
    {
        var registry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("unused"));
        var engine = CreateEngine(registry);
        var task = CreateTask(
            [Step("filesystem.list", "blocked", """{ "path": "." }""")]) with
        {
            Tools = [],
        };

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Steps[0].Error.Should().BeOfType<WorkflowValidationException>();
    }

    /// <summary>
    /// Verifies a skill without args.text receives the preceding output automatically.
    /// </summary>
    [Fact]
    public async Task SkillWithoutTextReceivesPreviousOutput()
    {
        var registry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("""{"messages":["hello"]}"""));
        var skill = new RecordingSkill("summarize", "summary");
        var engine = CreateEngine(registry, [skill]);
        var task = CreateTask(
            [
                Step("filesystem.list", "input", """{ "path": "." }"""),
                new WorkflowStepDefinition
                {
                    Step = "ai.summarize",
                    IsStringForm = true,
                },
            ]);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        skill.Inputs.Should().Equal("""{"messages":["hello"]}""");
        result.FinalText.Should().Be("""{"text":"summary"}""");
    }

    /// <summary>
    /// Verifies a first skill without input receives the task prompt.
    /// </summary>
    [Fact]
    public async Task FirstSkillWithoutTextReceivesTaskPrompt()
    {
        var registry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("unused"));
        var skill = new RecordingSkill("summarize", "summary");
        var engine = CreateEngine(registry, [skill]);
        var task = CreateTask(
            [
                new WorkflowStepDefinition
                {
                    Step = "ai.summarize",
                    IsStringForm = true,
                },
            ]);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue();
        skill.Inputs.Should().Equal("original prompt");
    }

    /// <summary>
    /// Verifies prompt templates resolve immediately before the prompt step runs.
    /// </summary>
    [Fact]
    public async Task PromptStepResolvesPreviousAndTaskPlaceholders()
    {
        var registry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("tool-output"));
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        agent.Setup(
                service => service.RunPromptAsync(
                    "tool-output for Workflow test",
                    It.IsAny<IEnumerable<string>>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult("prompt-output", "Test"));
        var engine = CreateEngine(registry, agentService: agent.Object);
        var task = CreateTask(
            [
                Step("filesystem.list", "input", """{ "path": "." }"""),
                Step(
                    "ai.prompt",
                    "prompt",
                    """{ "prompt": "{{previous.output}} for {{task.name}}" }"""),
            ]);

        var result = await engine.ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FinalText.Should().Be("prompt-output");
        agent.VerifyAll();
    }

    private static SequentialWorkflowEngine CreateEngine(
        IToolRegistry registry,
        IEnumerable<ISkill>? skills = null,
        IMyLocalAgentService? agentService = null,
        TimeProvider? timeProvider = null)
    {
        var agent = agentService ?? Mock.Of<IMyLocalAgentService>();
        return new SequentialWorkflowEngine(
            registry,
            skills ?? [],
            agent,
            timeProvider ?? TimeProvider.System,
            NullLogger<SequentialWorkflowEngine>.Instance);
    }

    private static ScheduledTask CreateTask(
        IReadOnlyList<WorkflowStepDefinition> workflow,
        WorkflowErrorPolicy errorPolicy = WorkflowErrorPolicy.StopOnError) =>
        new()
        {
            Id = "workflow-test",
            Name = "Workflow test",
            Schedule = "0 7 * * *",
            Prompt = "original prompt",
            Tools = [ToolGroups.FileSystem],
            Workflow = workflow,
            Enabled = true,
            ErrorPolicy = errorPolicy,
        };

    private static WorkflowStepDefinition Step(
        string alias,
        string id,
        string argumentsJson,
        WorkflowErrorPolicy? onError = null,
        int? retryCount = null,
        int? retryBackoffSeconds = null)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return new WorkflowStepDefinition
        {
            Step = alias,
            Id = id,
            Args = document.RootElement.Clone(),
            OnError = onError,
            RetryCount = retryCount,
            RetryBackoffSeconds = retryBackoffSeconds,
        };
    }

    private static string CreateTaskJson(string stepJson, string toolGroup) =>
        $$"""
        {
          "id": "forms-test",
          "name": "Forms test",
          "schedule": "0 7 * * *",
          "prompt": "prompt",
          "tools": ["{{toolGroup}}"],
          "workflow": [{{stepJson}}],
          "enabled": true,
          "errorPolicy": "StopOnError"
        }
        """;

    private sealed class RecordingToolRegistry : IToolRegistry
    {
        private readonly Func<string, JsonElement, int, Task<string>> _handler;

        public RecordingToolRegistry(
            Func<string, JsonElement, int, Task<string>> handler)
        {
            _handler = handler;
        }

        public List<string> Aliases { get; } = [];

        public IReadOnlyList<ToolDefinition> Definitions => ToolMap.Definitions;

        public AITool[] GetAgentTools(IEnumerable<string> enabledToolGroups) => [];

        public bool TryGetDefinition(
            string alias,
            out ToolDefinition? definition)
        {
            definition = ToolMap.Find(alias);
            return definition is not null;
        }

        public Task<string> InvokeAsync(
            string alias,
            JsonElement arguments,
            IEnumerable<string> enabledToolGroups,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = ToolMap.Find(alias) ??
                throw new WorkflowValidationException($"Unknown alias '{alias}'.");
            if (!enabledToolGroups.Contains(
                    definition.ToolGroup,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new WorkflowValidationException(
                    $"Alias '{alias}' requires whitelisted tool group '{definition.ToolGroup}'.");
            }

            Aliases.Add(alias);
            return _handler(alias, arguments, Aliases.Count);
        }
    }

    private sealed class RecordingSkill : ISkill
    {
        private readonly string _output;

        public RecordingSkill(string name, string output)
        {
            Name = name;
            _output = output;
        }

        public string Name { get; }

        public string AgentFunctionName => $"skill_{Name}";

        public string Description => "Records supplied text for workflow tests.";

        public List<string> Inputs { get; } = [];

        public Task<string> ExecuteAsync(
            string text,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Add(text);
            return Task.FromResult(_output);
        }
    }

    private sealed class RecordingTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset InitialUtc =
            new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

        private long _timestamp;

        public List<TimeSpan> Delays { get; } = [];

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() =>
            InitialUtc + TimeSpan.FromTicks(_timestamp);

        public override long GetTimestamp() => _timestamp;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            Delays.Add(dueTime);
            _timestamp += dueTime.Ticks;
            callback(state);
            return new CompletedTimer();
        }
    }

    private sealed class CompletedTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
