using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.Workflows;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the production central adapter deterministically without network or model experiments.</summary>
public sealed class CentralDecisionTests
{
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private static JsonElement Choices => Json("""{"process":"Complete request.","review":"Requires review."}""");

    /// <summary>Strict validated choices expose no native scores and use one isolated, bounded request.</summary>
    [Fact]
    public async Task AcceptedCentralChoiceHasNoScoresOrHiddenContext()
    {
        using var h = new Harness();
        var result = await h.Evaluate();
        result.SchemaVersion.Should().Be(2);
        result.Engine.Should().Be("CentralLlm");
        result.AcceptanceBasis.Should().Be("validatedChoice");
        result.Capabilities.Scores.Should().BeFalse();
        result.Status.Should().Be("accepted");
        result.Choice.Should().Be("review");
        result.Probabilities.Should().BeNull();
        result.TopProbability.Should().BeNull();
        result.Margin.Should().BeNull();
        result.Confidence.Should().BeNull();
        result.Acceptance.Should().BeNull();
        h.Calls.Should().Be(1);
        h.Messages.Should().HaveCount(2);
        h.Messages![1].Text.Should().Contain("Explicit source").And.NotContain("SECRET TASK").And.NotContain("SECRET HISTORY");
        h.Request!.Tools.Should().BeNullOrEmpty();
        h.Request.ModelId.Should().Be("central-model");
        h.Request.MaxOutputTokens.Should().Be(512);
        h.Request.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>();
        h.Native.VerifyNoOtherCalls();
        var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.GetProperty("probabilities").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("acceptance").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Unknown, duplicated, missing, explanatory, and self-scored output fails strictly with no fallback.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"decision":"unknown"}""")]
    [InlineData("""{"decision":"review","confidence":0.99}""")]
    [InlineData("""{"decision":"review","decision":"process"}""")]
    [InlineData("```json\n{\"decision\":\"review\"}\n```")]
    public async Task MalformedCentralOutputIsUnavailable(string output)
    {
        using var h = new Harness();
        h.Output = output;
        var result = await h.Evaluate();
        result.Status.Should().Be("unavailable");
        result.Reasons.Should().Equal("InvalidResponse");
        result.Choice.Should().BeNull();
        result.AcceptanceBasis.Should().Be("validatedChoice");
        h.Calls.Should().Be(1);
        h.Native.VerifyNoOtherCalls();
    }

    /// <summary>Explicit native gates remain authoring errors rather than silently disappearing on a mode switch.</summary>
    [Fact]
    public async Task ExplicitAcceptanceBlocksBeforeInference()
    {
        using var h = new Harness();
        await FluentActions.Awaiting(() => h.Service.EvaluateAsync("source", "question", Choices,
            Json("{}"), default)).Should().ThrowAsync<WorkflowValidationException>().WithMessage("*incompatible*");
        h.Calls.Should().Be(0);
    }

    /// <summary>A captured run keeps engine, model, and limits even when Settings change during its first call.</summary>
    [Fact]
    public async Task SnapshotSurvivesGlobalChangesAcrossTools()
    {
        using var h = new Harness();
        var snapshot = h.Service.Capture();
        h.Options.Set(new() { Enabled = true, Engine = "NativeDecision", DecisionModel = "missing-model" });
        h.Factory.Setup(f => f.Capture()).Throws(new InvalidOperationException("Must not resolve again"));
        var context = new ToolExecutionContext("task", [], [], surface: ToolInvocationSurface.Workflow) { DecisionSnapshot = snapshot };
        foreach (var tool in new[] { new DecisionTool(context, h.Service), new DecisionTool(context.WithKnowledgeScope([]), h.Service) })
        {
            var result = Json(await tool.EvaluateAsync("source", "question", Choices));
            result.GetProperty("engine").GetString().Should().Be("CentralLlm");
            result.GetProperty("model").GetString().Should().Be("central-model");
        }
        h.Factory.Verify(f => f.Capture(), Times.Once);
        h.Calls.Should().Be(2);
    }

    /// <summary>Full prompt budgeting uses the central context and output reserve, independently from invalid native settings.</summary>
    [Fact]
    public async Task CentralBudgetDoesNotInheritNativeCeiling()
    {
        using var h = new Harness();
        h.Options.Set(h.Options.CurrentValue with { DecisionModel = "", MaxInputTokens = 1, DecisionColdTimeoutSeconds = -1 });
        (await h.Service.EvaluateAsync(new string('x', 3000), "question", Choices, null, default)).Status.Should().Be("accepted");
        var oversized = await h.Service.EvaluateAsync(new string('x', 20000), "question", Choices, null, default);
        oversized.Status.Should().Be("unavailable");
        oversized.Reasons.Should().Equal("InputTooLarge");
        h.Calls.Should().Be(1);
        h.Native.VerifyNoOtherCalls();
    }

    /// <summary>All Gmail answer modes retain semantic types without constructing distributions.</summary>
    [Fact]
    public async Task MultipleChoiceBooleanAndRubricQuestionsRemainTyped()
    {
        using var h = new Harness();
        h.Output = """{"category":"information","reply":false,"priority":1}""";
        var set = DecisionQuestionSet.Parse(Json(GmailDecisionDefaults.QuestionsJson));
        var result = await h.Service.EvaluateQuestionsAsync(h.Service.Capture(), "source", set, true, default);
        result.Status.Should().Be("accepted");
        result.NativePayload.Should().BeNull();
        result.Answers!.Value.GetProperty("reply").GetBoolean().Should().BeFalse();
        result.Answers.Value.GetProperty("priority").GetInt32().Should().Be(1);
    }

    /// <summary>Raw mail cannot cross the local-only boundary even when a valid cloud provider is selected.</summary>
    [Fact]
    public async Task CloudRawMailIsBlockedWithoutAnyCall()
    {
        using var h = new Harness();
        h.Factory.Setup(f => f.Capture()).Returns(new LlmClientSnapshot("OpenAI", "central-model", h.Chat.Object, false));
        var result = await h.Service.EvaluateQuestionsAsync(h.Service.Capture(), "RAW MAIL",
            DecisionQuestionSet.Parse(Json(GmailDecisionDefaults.QuestionsJson)), true, default);
        result.Reasons.Should().Equal("LocalSourceRequired");
        h.Calls.Should().Be(0);
    }

    /// <summary>Cancellation propagates without a result or another provider call.</summary>
    [Fact]
    public async Task CancellationPropagates()
    {
        using var h = new Harness();
        using var cancellation = new CancellationTokenSource();
        h.Chat.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((_, _, token) =>
            { cancellation.Cancel(); return Task.FromCanceled<ChatResponse>(token); });
        await FluentActions.Awaiting(() => h.Evaluate(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        h.Native.VerifyNoOtherCalls();
    }

    /// <summary>Unsupported selected capabilities do not cause alternate-provider selection.</summary>
    [Fact]
    public async Task UnsupportedProviderFailsWithoutSubstitution()
    {
        using var h = new Harness();
        h.Factory.Setup(f => f.Capture()).Throws(new NotSupportedException());
        var result = await h.Evaluate();
        result.Status.Should().Be("unavailable");
        result.Reasons.Should().Equal("UnsupportedCapability");
        h.Calls.Should().Be(0);
        h.Native.VerifyNoOtherCalls();
    }

    /// <summary>Loading legacy/canonical profiles without an engine preserves native behavior; new profiles explicitly select central.</summary>
    [Theory]
    [InlineData("{}", "tev1:latest")]
    [InlineData("""{"GmailDecision":{"DecisionModel":"legacy"}}""", "legacy")]
    [InlineData("""{"Decision":{"DecisionModel":"canonical"}}""", "canonical")]
    [InlineData("""{"Decision":{"DecisionModel":"canonical"},"GmailDecision":{"DecisionModel":"legacy"}}""", "canonical")]
    public void ProfileMigrationPreservesNative(string json, string model)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var settings = DecisionOptions.Resolve(configuration);
        settings.Engine.Should().Be("NativeDecision");
        settings.DecisionModel.Should().Be(model);
        settings.Enabled.Should().BeFalse();
        DecisionOptions.NewProfile().Engine.Should().Be("CentralLlm");
        DecisionOptions.NewProfile().Enabled.Should().BeFalse();
    }

    /// <summary>Legacy Gmail selectors are compatible only when they match the global engine and carry no secondary-engine policies.</summary>
    [Theory]
    [InlineData("Hybrid", "NativeDecision", true)]
    [InlineData("LlmOnly", "CentralLlm", true)]
    [InlineData("Hybrid", "CentralLlm", false)]
    [InlineData("LlmOnly", "NativeDecision", false)]
    public void LegacyGmailMappingIsExplicit(string mode, string engine, bool compatible)
    {
        var action = () => GmailDecisionMigration.Validate(mode, null, null, engine, null);
        if (compatible) action.Should().NotThrow();
        else action.Should().Throw<WorkflowValidationException>().WithMessage("*conflicts*global*");
        FluentActions.Invoking(() => GmailDecisionMigration.Validate("Global", Json("{}"), null, engine, null))
            .Should().Throw<WorkflowValidationException>();
    }

    /// <summary>Authored native numeric consumers are rejected before inference unless a capability guard dominates the route.</summary>
    [Theory]
    [InlineData("decision.evaluate", "topProbability", "greaterThan")]
    [InlineData("decision.evaluate", "probabilities.process", "equals")]
    [InlineData("gmailDecision.readClassify", "messages[0].decisionScores.category.top", "greaterThan")]
    [InlineData("gmailDecision.readClassify", "messages[0].decisionScores.reply.noul", "equals")]
    public void NumericGateRequiresDominatingCapabilityGuard(string tool, string path, string comparison)
    {
        using var h = new Harness();
        var producer = new WorkflowStepDefinition { Step = tool, Id = "result", Args = JsonSerializer.SerializeToElement(new { source = "s", question = "q", options = Choices }) };
        var gate = new WorkflowStepDefinition { Step = "flow.decision", Id = "numeric", Args = JsonSerializer.SerializeToElement(new { source = "{{result.output}}", path, @operator = comparison, value = 0.9, target = "@end" }) };
        FluentActions.Invoking(() => DecisionWorkflowCompatibility.Validate([producer, gate], h.Service.Capture()))
            .Should().Throw<WorkflowValidationException>().WithMessage("*capabilities.scores*");
        var capability = new WorkflowStepDefinition { Step = "flow.decision", Id = "capability", Next = "@end", Args = Json("""{"source":"{{result.output}}","path":"capabilities.scores","operator":"equals","value":true,"target":"numeric"}""") };
        FluentActions.Invoking(() => DecisionWorkflowCompatibility.Validate([producer, capability, gate], h.Service.Capture())).Should().NotThrow();
        FluentActions.Invoking(() => DecisionWorkflowCompatibility.Validate([producer, capability with { Next = "numeric" }, gate], h.Service.Capture()))
            .Should().Throw<WorkflowValidationException>();
        h.Calls.Should().Be(0);
    }

    /// <summary>The explicit model supplied to the central transport defeats unrelated conversation model scopes.</summary>
    [Fact]
    public async Task ConversationModelScopeCannotOverrideGlobalDecisionModel()
    {
        using var h = new Harness();
        using var model = ModelExecutionScope.Push("conversation-model");
        var result = await h.Evaluate();
        result.Model.Should().Be("central-model");
        h.Request!.ModelId.Should().Be("central-model");
    }

    /// <summary>Remote model aliases at a loopback endpoint cannot receive raw Gmail text.</summary>
    [Fact]
    public async Task LocalModelVerificationBlocksRemoteAlias()
    {
        using var h = new Harness();
        h.Factory.Setup(f => f.Capture()).Returns(new LlmClientSnapshot("Ollama", "remote-alias", h.Chat.Object, true)
        { ValidateLocalSourceAsync = _ => Task.FromResult(false) });
        var result = await h.Service.EvaluateQuestionsAsync(h.Service.Capture(), "RAW MAIL",
            DecisionQuestionSet.Parse(Json(GmailDecisionDefaults.QuestionsJson)), true, default);
        result.Reasons.Should().Equal("LocalSourceRequired");
        h.Calls.Should().Be(0);
    }
    /// <summary>The real workflow captures once and takes its authored no-score route even after settings change between decisions.</summary>
    [Fact]
    public async Task WorkflowFreezesEngineAndRoutesSampleWithoutScores()
    {
        using var h = new Harness();
        h.Output = """{"decision":"process"}""";
        h.AfterCall = () => h.Options.Set(h.Options.CurrentValue with { Engine = "NativeDecision" });
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OD.MyLocalAgent.sln"))) root = root.Parent;
        var task = ScheduledTaskSerializer.Deserialize(File.ReadAllText(Path.Combine(root!.FullName, "Docs", "samples", "decision-native-score.json")));
        task.Enabled.Should().BeFalse();
        task = task with { Workflow = [task.Workflow[0] with { Id = "first" }, .. task.Workflow] };
        using var provider = new ServiceCollection().AddSingleton<IDecisionEvaluationService>(h.Service).BuildServiceProvider();
        var registry = new ToolRegistry(ToolCatalog.Default, new ToolTargetActivator(provider, ToolCatalog.Default));
        var engine = new SequentialWorkflowEngine(registry, [new EchoSkill()], Mock.Of<IMyLocalAgentService>(),
            TimeProvider.System, NullLogger<SequentialWorkflowEngine>.Instance, decisionEvaluation: h.Service);
        var result = await engine.ExecuteAsync(task, default);
        result.Success.Should().BeTrue();
        result.FinalText.Should().Contain("no native scores").And.Contain("human review");
        h.Factory.Verify(f => f.Capture(), Times.Once);
        h.Calls.Should().Be(2);
        h.Native.VerifyNoOtherCalls();
        h.Service.Capture().Engine.Should().Be("NativeDecision");
    }

    private sealed class EchoSkill : ISkill
    {
        public string Name => "summarize";
        public string AgentFunctionName => "test_echo";
        public string Description => "Returns the authored branch without inference.";
        public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken) => Task.FromResult(input);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "central-decision-" + Guid.NewGuid().ToString("N"));
        public MutableOptionsMonitor<DecisionOptions> Options { get; } = new(new() { Enabled = true, Engine = "CentralLlm" });
        public Mock<ILocalDecisionClient> Native { get; } = new(MockBehavior.Strict);
        public Mock<IChatClient> Chat { get; } = new();
        public Mock<ILlmClientFactory> Factory { get; } = new();
        public DecisionEvaluationService Service { get; }
        public string Output { get; set; } = """{"decision":"review"}""";
        public int Calls { get; private set; }
        public IReadOnlyList<ChatMessage>? Messages { get; private set; }
        public ChatOptions? Request { get; private set; }
        public Action? AfterCall { get; set; }
        public Harness()
        {
            Chat.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Returns<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((messages, request, _) =>
                {
                    Calls++;
                    Messages = messages.ToArray();
                    Request = request;
                    AfterCall?.Invoke();
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Output)));
                });
            Factory.Setup(f => f.Capture()).Returns(new LlmClientSnapshot("Ollama", "central-model", Chat.Object, true));
            Service = new(Options, new MutableOptionsMonitor<OllamaOptions>(new()), new MutableOptionsMonitor<KnowledgeOptions>(new()),
                Native.Object, new DecisionModelResidencyCoordinator(_directory, TimeProvider.System), TimeProvider.System,
                NullLogger<DecisionEvaluationService>.Instance, Factory.Object,
                new MutableOptionsMonitor<AIOptions>(new() { GenerationDefaults = new() { ContextWindowTokens = 8192 }, AgentSystemPrompt = "SECRET TASK", DirectChatSystemPrompt = "SECRET HISTORY" }));
        }
        public Task<DecisionEvaluationResult> Evaluate(CancellationToken cancellationToken = default) =>
            Service.EvaluateAsync("Explicit source", "Is it complete?", Choices, null, cancellationToken);
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
