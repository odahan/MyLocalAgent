using System.Text.Json;
using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Workflows;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
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
    /// Verifies the observable workflow context is refreshed after every successful step.
    /// </summary>
    [Fact]
    public async Task PublishesWorkflowContextAfterEverySuccessfulStep()
    {
        var registry = new RecordingToolRegistry(
            (alias, _, _) => Task.FromResult($"output:{alias}"));
        var engine = CreateEngine(registry);
        var task = CreateTask(
            [
                Step("filesystem.list", "first", "{}"),
                Step("filesystem.list", "second", "{}"),
            ]);
        var contexts = new List<string>();

        var result = await engine.ExecuteAsync(
            task,
            contexts.Add,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        contexts.Should().HaveCount(2);
        contexts[0].Should().Contain("first").And.NotContain("second");
        contexts[1].Should().Contain("first").And.Contain("second");
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
                service => service.RunPromptWithoutToolsAsync(
                    "tool-output for Workflow test",
                    It.IsAny<EffectiveGenerationPolicy>(),
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

    /// <summary>
    /// Verifies workflow placeholders expose the active model and step-effective settings.
    /// </summary>
    [Fact]
    public async Task WorkflowResolvesModelInformationFromStepEffectivePolicy()
    {
        const string expected =
            "ModelName qwen3:14b - Temp 0.7 - TopP 0.85 - Context 12000";
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        agent.Setup(
                service => service.RunPromptWithoutToolsAsync(
                    expected,
                    It.Is<EffectiveGenerationPolicy>(
                        policy =>
                            policy.Temperature == 0.7 &&
                            policy.TopP == 0.85 &&
                            policy.ContextWindowTokens == 12_000),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult("done", "Test"));
        var provider = new StubLlmClientService("unused")
        {
            ModelName = "qwen3:14b",
        };
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var options = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults
                {
                    Temperature = 0.2,
                    TopP = 1,
                    ContextWindowTokens = 12_000,
                },
            });
        var definition = Step(
            "ai.prompt",
            "model-info",
            """{ "prompt": "{{ModelInfo}}" }""") with
        {
            GenerationOptions = new GenerationOptions
            {
                Temperature = 0.7,
            },
        };
        var task = CreateTask([definition]) with
        {
            GenerationOptions = new GenerationOptions
            {
                TopP = 0.85,
            },
        };

        var result = await CreateEngine(
                new RecordingToolRegistry(
                    (_, _, _) => Task.FromResult(string.Empty)),
                agentService: agent.Object,
                aiOptions: options,
                llmClientFactory: factory.Object)
            .ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue();
        agent.VerifyAll();
        factory.VerifyAll();
    }

    /// <summary>
    /// Verifies placeholders can select object properties and array items from JSON outputs.
    /// </summary>
    [Fact]
    public async Task ResolvesStructuredOutputPaths()
    {
        var registry = new RecordingToolRegistry(
            (alias, arguments, _) =>
            {
                if (alias == "filesystem.list")
                {
                    return Task.FromResult(
                        """{"path":"inbox","entries":[{"name":"source.txt","type":"file"}]}""");
                }

                arguments.GetProperty("path").GetString()
                    .Should().Be("inbox/source.txt");
                arguments.GetProperty("content").GetString()
                    .Should().Be("source.txt");
                return Task.FromResult("""{"path":"inbox/source.txt","bytes":10}""");
            });
        var task = CreateTask(
            [
                Step("filesystem.list", "files", """{ "path": "inbox" }"""),
                Step(
                    "filesystem.write",
                    "save",
                    """
                    {
                      "path": "{{files.output.path}}/{{files.output.entries[0].name}}",
                      "content": "{{previous.output.entries[0].name}}"
                    }
                    """),
            ]);

        var result = await CreateEngine(registry).ExecuteAsync(
            task,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        registry.Aliases.Should().Equal("filesystem.list", "filesystem.write");
    }

    /// <summary>
    /// Verifies prompt steps can receive the accumulated structured workflow context.
    /// </summary>
    [Fact]
    public async Task PromptStepCanIncludeAccumulatedContext()
    {
        var registry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult(
                """{"path":"inbox/source.txt","content":"Bonjour"}"""));
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        agent.Setup(
                service => service.RunPromptWithoutToolsAsync(
                    It.Is<string>(
                        prompt =>
                            prompt.StartsWith("Translate into English.") &&
                            prompt.Contains("\"source\"") &&
                            prompt.Contains("\"content\": \"Bonjour\"")),
                    It.IsAny<EffectiveGenerationPolicy>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult("Hello", "Test"));
        var task = CreateTask(
            [
                Step(
                    "filesystem.read",
                    "source",
                    """{ "path": "inbox/source.txt" }"""),
                Step(
                    "ai.prompt",
                    "translation",
                    """
                    {
                      "prompt": "Translate into English.",
                      "includeContext": true
                    }
                    """),
            ]);

        var result = await CreateEngine(
                registry,
                agentService: agent.Object)
            .ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FinalText.Should().Be("Hello");
        agent.VerifyAll();
    }

    /// <summary>
    /// Verifies invalid structured selectors fail before the following tool is invoked.
    /// </summary>
    [Fact]
    public async Task InvalidStructuredOutputPathFailsStep()
    {
        var registry = new RecordingToolRegistry(
            (alias, _, _) => Task.FromResult(
                alias == "filesystem.list"
                    ? """{"entries":[]}"""
                    : "unexpected"));
        var task = CreateTask(
            [
                Step("filesystem.list", "files", """{ "path": "inbox" }"""),
                Step(
                    "filesystem.read",
                    "read",
                    """{ "path": "{{files.output.entries[0].name}}" }"""),
            ]);

        var result = await CreateEngine(registry).ExecuteAsync(
            task,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Steps[1].Error.Should().BeOfType<WorkflowPlaceholderException>();
        registry.Aliases.Should().Equal("filesystem.list");
    }

    /// <summary>
    /// Verifies the context snapshot preserves JSON outputs and quotes plain text.
    /// </summary>
    [Fact]
    public void WorkflowContextSerializesStructuredAndPlainOutputs()
    {
        var context = new WorkflowContext();
        context.RecordOutput("source", """{"content":"Bonjour"}""");
        context.RecordOutput("translation", "Hello");

        using var document = JsonDocument.Parse(context.ToJson());

        document.RootElement.GetProperty("steps")
            .GetProperty("source")
            .GetProperty("content")
            .GetString()
            .Should().Be("Bonjour");
        document.RootElement.GetProperty("steps")
            .GetProperty("translation")
            .GetString()
            .Should().Be("Hello");
    }

    /// <summary>
    /// Verifies prompt context inclusion uses a JSON boolean rather than a string flag.
    /// </summary>
    [Fact]
    public void PromptContextFlagMustBeBoolean()
    {
        var task = CreateTask(
            [
                Step(
                    "ai.prompt",
                    "prompt",
                    """{ "prompt": "Translate.", "includeContext": "true" }"""),
            ]);

        var action = () => ScheduledTaskValidator.Validate(task);

        action.Should()
            .Throw<WorkflowValidationException>()
            .WithMessage("*includeContext*boolean*");
    }

    /// <summary>
    /// Verifies local date and time placeholders support defaults and filename-safe formats.
    /// </summary>
    [Fact]
    public async Task ResolvesTodayAndNowWithFriendlyFormats()
    {
        var timeProvider = new RecordingTimeProvider();
        var registry = new RecordingToolRegistry(
            (_, arguments, _) =>
            {
                arguments.GetProperty("path").GetString()
                    .Should().Be("reports/2026/20260729-120000.txt");
                arguments.GetProperty("content").GetString()
                    .Should().Be("2026-07-29|2026-07-29T12:00:00+00:00");
                return Task.FromResult("ok");
            });
        var task = CreateTask(
            [
                Step(
                    "filesystem.write",
                    "dated",
                    """
                    {
                      "path": "reports/{{Today[YYYY]}}/{{Today[yyyyMMdd]}}-{{Now[hhmmss]}}.txt",
                      "content": "{{Today}}|{{Now}}"
                    }
                    """),
            ]);

        var result = await CreateEngine(
                registry,
                timeProvider: timeProvider)
            .ExecuteAsync(task, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    /// <summary>
    /// Verifies deterministic tool calls receive the task's file-system grants.
    /// </summary>
    [Fact]
    public async Task ToolStepReceivesTaskExecutionContext()
    {
        var registry = new RecordingToolRegistry(
            (_, _, _) => Task.FromResult("ok"));
        var authorizedPath = Path.Combine(Path.GetTempPath(), "workflow-authorized");
        var task = CreateTask(
            [Step("filesystem.list", "list", """{ "path": "." }""")]) with
        {
            FileSystemAccess =
            [
                new FileSystemAccessRule
                {
                    Path = authorizedPath,
                    Access = FileSystemAccessMode.ReadOnly,
                },
            ],
        };

        var result = await CreateEngine(registry).ExecuteAsync(
            task,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        registry.Contexts.Should().ContainSingle();
        registry.Contexts[0].TaskId.Should().Be("workflow-test");
        registry.Contexts[0].FileSystemAccess.Should().ContainSingle(
            rule => rule.Path == authorizedPath);
    }

    /// <summary>
    /// Verifies direct built-in skill steps receive field-wise step and task values.
    /// </summary>
    [Fact]
    public async Task AiSkillStepReceivesEffectiveGenerationPolicy()
    {
        var skill = new RecordingSkill("summarize", "summary");
        var options = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults
                {
                    Temperature = 0.2,
                    TopP = 1,
                    ContextWindowTokens = 16_384,
                },
            });
        var definition = Step(
            "ai.summarize",
            "summary",
            """{"text":"input"}""") with
        {
            GenerationOptions = new GenerationOptions { TopP = 0.8 },
        };
        var task = CreateTask([definition]) with
        {
            GenerationOptions =
                new GenerationOptions { Temperature = 0.4 },
        };

        var result = await CreateEngine(
            new RecordingToolRegistry(
                (_, _, _) => Task.FromResult(string.Empty)),
            [skill],
            aiOptions: options).ExecuteAsync(
                task,
                CancellationToken.None);

        result.Success.Should().BeTrue();
        skill.Policies.Should().ContainSingle();
        skill.Policies[0].Temperature.Should().Be(0.4);
        skill.Policies[0].TemperatureSource.Should()
            .Be(GenerationOptionSource.Task);
        skill.Policies[0].TopP.Should().Be(0.8);
        skill.Policies[0].TopPSource.Should()
            .Be(GenerationOptionSource.Step);
        skill.Policies[0].ContextWindowTokens.Should().Be(16_384);
    }

    /// <summary>
    /// Verifies ai.prompt receives its step-effective policy without exposing task tools.
    /// </summary>
    [Fact]
    public async Task AiPromptReceivesStepEffectivePolicyWithoutTools()
    {
        EffectiveGenerationPolicy? capturedPolicy = null;
        var agent = new Mock<IMyLocalAgentService>(MockBehavior.Strict);
        agent.Setup(
                service => service.RunPromptWithoutToolsAsync(
                    "Draft the report.",
                    It.IsAny<EffectiveGenerationPolicy>(),
                    It.IsAny<CancellationToken>()))
            .Callback<
                string,
                EffectiveGenerationPolicy,
                CancellationToken>(
                (_, policy, _) => capturedPolicy = policy)
            .ReturnsAsync(new AgentRunResult("draft", "Test"));
        var options = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults
                {
                    Temperature = 0.2,
                    TopP = 1,
                    ContextWindowTokens = 12_000,
                },
            });
        var definition = Step(
            "ai.prompt",
            "draft",
            """{"prompt":"Draft the report."}""") with
        {
            GenerationOptions = new GenerationOptions
            {
                Temperature = 0.7,
            },
        };
        var task = CreateTask([definition]) with
        {
            GenerationOptions = new GenerationOptions
            {
                TopP = 0.85,
            },
        };

        var result = await CreateEngine(
            new RecordingToolRegistry(
                (_, _, _) => Task.FromResult(string.Empty)),
            agentService: agent.Object,
            aiOptions: options).ExecuteAsync(
                task,
                CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedPolicy.Should().NotBeNull();
        capturedPolicy!.Temperature.Should().Be(0.7);
        capturedPolicy.TemperatureSource.Should().Be(
            GenerationOptionSource.Step);
        capturedPolicy.TopP.Should().Be(0.85);
        capturedPolicy.TopPSource.Should().Be(GenerationOptionSource.Task);
        capturedPolicy.ContextWindowTokens.Should().Be(12_000);
        agent.VerifyAll();
    }

    /// <summary>
    /// Verifies workflow retries retain the policy resolved when the step began.
    /// </summary>
    [Fact]
    public async Task RetryRetainsResolvedPolicyAfterSettingsChange()
    {
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
        var skill = new SettingsChangingSkill(options);
        var definition = Step(
            "ai.summarize",
            "summary",
            """{"text":"input"}""",
            WorkflowErrorPolicy.Retry,
            retryCount: 1,
            retryBackoffSeconds: 0);

        var result = await CreateEngine(
            new RecordingToolRegistry(
                (_, _, _) => Task.FromResult(string.Empty)),
            [skill],
            aiOptions: options).ExecuteAsync(
                CreateTask([definition]),
                CancellationToken.None);

        result.Success.Should().BeTrue();
        skill.Policies.Should().HaveCount(2);
        skill.Policies.Should().OnlyContain(
            static policy =>
                policy.Temperature == 0.2 &&
                policy.ContextWindowTokens == 8192);
    }

    /// <summary>
    /// Verifies concurrent tasks cannot observe one another's generation overrides.
    /// </summary>
    [Fact]
    public async Task ConcurrentTaskGenerationPoliciesRemainIsolated()
    {
        var skill = new ConcurrentPolicySkill();
        var options = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults(),
            });
        var engine = CreateEngine(
            new RecordingToolRegistry(
                (_, _, _) => Task.FromResult(string.Empty)),
            [skill],
            aiOptions: options);
        var first = CreateTask(
            [Step("ai.summarize", "first", """{"text":"first"}""")]) with
        {
            Id = "first-task",
            GenerationOptions = new GenerationOptions
            {
                Temperature = 0.1,
                TopP = 0.8,
            },
        };
        var second = CreateTask(
            [Step("ai.summarize", "second", """{"text":"second"}""")]) with
        {
            Id = "second-task",
            GenerationOptions = new GenerationOptions
            {
                Temperature = 0.9,
                TopP = 0.6,
            },
        };

        var results = await Task.WhenAll(
            engine.ExecuteAsync(first, CancellationToken.None),
            engine.ExecuteAsync(second, CancellationToken.None));

        results.Should().OnlyContain(static result => result.Success);
        skill.Policies["first"].Temperature.Should().Be(0.1);
        skill.Policies["first"].TopP.Should().Be(0.8);
        skill.Policies["second"].Temperature.Should().Be(0.9);
        skill.Policies["second"].TopP.Should().Be(0.6);
    }

    private static SequentialWorkflowEngine CreateEngine(
        IToolRegistry registry,
        IEnumerable<ISkill>? skills = null,
        IMyLocalAgentService? agentService = null,
        TimeProvider? timeProvider = null,
        IOptionsMonitor<AIOptions>? aiOptions = null,
        ILlmClientFactory? llmClientFactory = null)
    {
        var agent = agentService ?? Mock.Of<IMyLocalAgentService>();
        return new SequentialWorkflowEngine(
            registry,
            skills ?? [],
            agent,
            timeProvider ?? TimeProvider.System,
            NullLogger<SequentialWorkflowEngine>.Instance,
            aiOptions,
            llmClientFactory);
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

        public List<ToolExecutionContext> Contexts { get; } = [];

        public IReadOnlyList<ToolDefinition> Definitions => ToolMap.Definitions;

        public AITool[] GetAgentTools(IEnumerable<string> enabledToolGroups) => [];

        public AITool[] GetAgentTools(
            IEnumerable<string> enabledToolGroups,
            ToolExecutionContext executionContext) => [];

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
            => InvokeAsync(
                alias,
                arguments,
                enabledToolGroups,
                ToolExecutionContext.Taskless,
                cancellationToken);

        public Task<string> InvokeAsync(
            string alias,
            JsonElement arguments,
            IEnumerable<string> enabledToolGroups,
            ToolExecutionContext executionContext,
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
            Contexts.Add(executionContext);
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

        public List<EffectiveGenerationPolicy> Policies { get; } = [];

        public Task<string> ExecuteAsync(
            string text,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Add(text);
            return Task.FromResult(_output);
        }

        public Task<string> ExecuteAsync(
            string text,
            EffectiveGenerationPolicy generationPolicy,
            CancellationToken cancellationToken)
        {
            Policies.Add(generationPolicy);
            return ExecuteAsync(text, cancellationToken);
        }
    }

    private sealed class SettingsChangingSkill : ISkill
    {
        private readonly MutableOptionsMonitor<AIOptions> _options;
        private int _attempt;

        public SettingsChangingSkill(
            MutableOptionsMonitor<AIOptions> options)
        {
            _options = options;
        }

        public string Name => "summarize";

        public string AgentFunctionName => "skill_summarize";

        public string Description => "Changes Settings during the first attempt.";

        public List<EffectiveGenerationPolicy> Policies { get; } = [];

        public Task<string> ExecuteAsync(
            string text,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The explicit policy overload is required.");

        public Task<string> ExecuteAsync(
            string text,
            EffectiveGenerationPolicy generationPolicy,
            CancellationToken cancellationToken)
        {
            Policies.Add(generationPolicy);
            if (Interlocked.Increment(ref _attempt) == 1)
            {
                _options.Set(
                    new AIOptions
                    {
                        GenerationDefaults = new AIGenerationDefaults
                        {
                            Temperature = 1.5,
                            TopP = 0.5,
                            ContextWindowTokens = 65_536,
                        },
                    });
                throw new InvalidOperationException("First attempt fails.");
            }

            return Task.FromResult("ok");
        }
    }

    private sealed class ConcurrentPolicySkill : ISkill
    {
        public string Name => "summarize";

        public string AgentFunctionName => "skill_summarize";

        public string Description => "Records concurrent generation policies.";

        public ConcurrentDictionary<string, EffectiveGenerationPolicy> Policies
        {
            get;
        } = new(StringComparer.Ordinal);

        public Task<string> ExecuteAsync(
            string text,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The explicit policy overload is required.");

        public Task<string> ExecuteAsync(
            string text,
            EffectiveGenerationPolicy generationPolicy,
            CancellationToken cancellationToken)
        {
            Policies[text] = generationPolicy;
            return Task.FromResult(text);
        }
    }

    private sealed class RecordingTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset InitialUtc =
            new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

        private long _timestamp;

        public List<TimeSpan> Delays { get; } = [];

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

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
