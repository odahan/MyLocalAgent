using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.Workflows;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies generic decisions, canonical settings and existing workflow routing without live inference.</summary>
public sealed class GeneralDecisionTests
{
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);
    private static JsonElement Choices => Json("""{"process":"Essential facts are present.","clarify":"Essential facts are missing."}""");
    private static JsonElement Payload(double top = 0.9, double? confidence = 0.8) => JsonSerializer.SerializeToElement(new
    {
        answers = new { decision = new { type = "choice", choice = "process",
            probabilities = new Dictionary<string, double> { ["process"] = top, ["clarify"] = 1 - top }, confidence } },
    });

    /// <summary>All four migration combinations resolve the same canonical values for generic and mail callers.</summary>
    [Theory]
    [InlineData(false, false, "tev1:latest", false)]
    [InlineData(true, false, "legacy:latest", false)]
    [InlineData(false, true, "canonical:latest", true)]
    [InlineData(true, true, "canonical:latest", true)]
    public void CanonicalMigrationPreservesMailPolicy(bool legacy, bool canonical, string model, bool enabled)
    {
        var values = new Dictionary<string, string?>();
        if (legacy)
        {
            values["GmailDecision:Enabled"] = "true";
            values["GmailDecision:DecisionModel"] = "legacy:latest";
            values["GmailDecision:KeepAliveSeconds"] = "3";
            values["GmailDecision:MinTopProbabilityPercent"] = "73.5";
            values["GmailDecision:BatchTimeoutSeconds"] = "600";
            values["GmailDecision:ArbitrationEnabled"] = "false";
            values["GmailDecision:FallbackModel"] = "fallback:latest";
        }
        if (canonical)
        {
            values["Decision:Enabled"] = "true";
            values["Decision:DecisionModel"] = "canonical:latest";
            values["Decision:KeepAliveSeconds"] = "7";
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var provider = new ServiceCollection().AddDecisionConfiguration(configuration).BuildServiceProvider();
        var decision = provider.GetRequiredService<IOptionsMonitor<DecisionOptions>>().CurrentValue;
        var mail = provider.GetRequiredService<IOptionsMonitor<GmailDecisionOptions>>().CurrentValue;
        decision.Should().BeEquivalentTo(DecisionOptions.Resolve(configuration));
        decision.Enabled.Should().Be(enabled);
        decision.DecisionModel.Should().Be(model);
        mail.DecisionModel.Should().Be(model);
        mail.KeepAliveSeconds.Should().Be(decision.KeepAliveSeconds);
        mail.MinTopProbabilityPercent.Should().Be(decision.MinTopProbabilityPercent);
        decision.OperationTimeoutSeconds.Should().Be(180);
        mail.Enabled.Should().Be(legacy);
        mail.ArbitrationEnabled.Should().Be(!legacy);
        mail.BatchTimeoutSeconds.Should().Be(legacy ? 600 : 180);
        mail.FallbackModel.Should().Be(legacy ? "fallback:latest" : null);
        decision.MinTopProbabilityPercent.Should().Be(legacy && !canonical ? 73.5 : 80);
    }

    /// <summary>An explicit empty section resets native defaults rather than inheriting stale legacy fields.</summary>
    [Fact]
    public void ExplicitEmptySectionIsAuthoritative()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"Decision":{},"GmailDecision":{"DecisionModel":"old:latest","KeepAliveSeconds":1}}"""));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        DecisionOptions.Resolve(configuration).DecisionModel.Should().Be("tev1:latest");
        using var provider = new ServiceCollection().AddDecisionConfiguration(configuration).BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<GmailDecisionOptions>>().CurrentValue.KeepAliveSeconds.Should().Be(10);
    }

    /// <summary>Settings reload updates both monitors using one authoritative provider configuration.</summary>
    [Fact]
    public void ReloadUpdatesGenericAndMailConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["GmailDecision:DecisionModel"] = "legacy:latest" }).Build();
        using var provider = new ServiceCollection().AddDecisionConfiguration(configuration).BuildServiceProvider();
        var generic = provider.GetRequiredService<IOptionsMonitor<DecisionOptions>>();
        var mail = provider.GetRequiredService<IOptionsMonitor<GmailDecisionOptions>>();
        generic.CurrentValue.DecisionModel.Should().Be("legacy:latest");
        mail.CurrentValue.DecisionModel.Should().Be("legacy:latest");
        configuration["Decision:DecisionModel"] = "new:latest";
        configuration.Reload();
        generic.CurrentValue.DecisionModel.Should().Be("new:latest");
        mail.CurrentValue.DecisionModel.Should().Be("new:latest");
    }

    /// <summary>Malformed authoring, duplicates and output-derived policy are rejected before inference.</summary>
    [Theory]
    [InlineData("""{"source":"s","question":"q","options":{"a":"A","A":"B"}}""")]
    [InlineData("""{"source":"s","question":"q","options":{"a":"A"}}""")]
    [InlineData("""{"source":"s","question":"q","options":{"a":"A","b":""}}""")]
    [InlineData("""{"source":"s","question":"","options":{"a":"A","b":"B"}}""")]
    [InlineData("""{"source":" ","question":"q","options":{"a":"A","b":"B"}}""")]
    [InlineData("""{"source":"s","question":"{{mail.output}}","options":{"a":"A","b":"B"}}""")]
    [InlineData("""{"source":"s","question":"q","options":{"a":"A","b":"B"},"model":"other"}""")]
    [InlineData("""{"source":"s","question":"q","options":{"a":"A","b":"B"},"acceptance":{"minMargin":1.1}}""")]
    [InlineData("""{"source":"s","question":"q","options":{"a":"A","b":"B"},"acceptance":{"minMargin":0.1,"minMargin":0.2}}""")]
    public void InvalidAuthoringFails(string args) =>
        FluentActions.Invoking(() => DecisionEvaluateContract.Validate(Json(args))).Should().Throw<WorkflowValidationException>();

    /// <summary>Valid scores are normalized, inclusive thresholds accept their boundary, and tighter overrides remain explicit.</summary>
    [Fact]
    public async Task AcceptanceInheritsAndConvertsPercentagesOnce()
    {
        using var h = new Harness(new() { Enabled = true, MinTopProbabilityPercent = 75, MinMarginPercentagePoints = 50 });
        h.Response = Payload(0.75);
        var result = await h.Evaluate();
        result.Status.Should().Be("accepted");
        result.Acceptance.MinTopProbability.Should().Be(0.75);
        result.Acceptance.MinMargin.Should().Be(0.5);
        result.TopProbability.Should().Be(0.75);
        result.Margin.Should().Be(0.5);
        result.Probabilities!.Values.Sum().Should().Be(1);
        result.Timing.GateWaitMs.Should().Be(2);
        result.Timing.InferenceMs.Should().Be(3);
        result.Timing.Cold.Should().BeTrue();
        h.Request!.Questions.GetProperty("decision").GetProperty("type").GetString().Should().Be("choice");
        (await h.Evaluate(acceptance: Json("""{"minTopProbability":0.8}"""))).Status.Should().Be("uncertain");
        await FluentActions.Awaiting(() => h.Evaluate(acceptance: Json("""{"minTopProbability":0.7}""")))
            .Should().ThrowAsync<WorkflowValidationException>();
        h.Client.Verify(c => c.ClassifyAsync(It.IsAny<LocalDecisionRequest>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Ties remain uncertain even with zero thresholds and retain their validated candidate and alternatives.</summary>
    [Fact]
    public async Task TiesNeverBecomeAccepted()
    {
        using var h = new Harness(new() { Enabled = true, MinTopProbabilityPercent = 0, MinMarginPercentagePoints = 0 });
        h.Response = Payload(0.5);
        var result = await h.Evaluate();
        result.Status.Should().Be("uncertain");
        result.Choice.Should().Be("process");
        result.Probabilities.Should().HaveCount(2);
        result.Reasons.Should().Contain("TiedTopScores");
    }

    /// <summary>The optional concentration check remains independent from the top answer score.</summary>
    [Fact]
    public async Task ConcentrationFailureIsUncertain()
    {
        using var h = new Harness(new() { Enabled = true, MinConfidencePercent = 90 });
        (await h.Evaluate()).Reasons.Should().Contain("ConcentrationBelowThreshold");
    }

    /// <summary>Malformed provider distributions and selected-top mismatches produce null scores, with cleanup.</summary>
    [Theory]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"process","probabilities":{"process":0.9,"clarify":0.9}}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"clarify","probabilities":{"process":0.9,"clarify":0.1}}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"process","probabilities":{"process":1.1,"clarify":-0.1}}}}""")]
    [InlineData("""{"answers":{}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"process","probabilities":{"process":0.9}}}}""")]
    [InlineData("""{"answers":{"decision":{"type":"choice","choice":"process","probabilities":{"process":0.9,"clarify":0.1,"other":0}}}}""")]
    public async Task InvalidProviderScoresAreUnavailable(string payload)
    {
        using var h = new Harness();
        h.Response = Json(payload);
        var result = await h.Evaluate();
        result.Status.Should().Be("unavailable");
        result.Choice.Should().BeNull();
        result.Probabilities.Should().BeNull();
        result.Margin.Should().BeNull();
        result.TopProbability.Should().BeNull();
        result.Confidence.Should().BeNull();
        h.Unloads.Should().Be(1);
    }

    /// <summary>Conservative budgeting includes Unicode source, options, question and reserve, and never clips content.</summary>
    [Fact]
    public async Task OversizedInputFailsBeforeCatalogOrInference()
    {
        using var h = new Harness();
        var source = new string('é', 850);
        var result = await h.Evaluate(source);
        result.Status.Should().Be("unavailable");
        result.Reasons.Should().Equal("InputTooLarge");
        h.Client.Verify(c => c.GetModelIdentitiesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Request.Should().BeNull();
        h.Unloads.Should().Be(0);
    }

    /// <summary>Missing models and unsafe primary-model aliases never trigger inference or cleanup.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelIdentityIsCheckedBeforeInference(bool shared)
    {
        using var h = new Harness();
        h.Identities = shared
            ? new Dictionary<string, string> { ["tev1:latest"] = "same", ["gemma4:12b"] = "same" }
            : new Dictionary<string, string>();
        var result = await h.Evaluate();
        result.Reasons.Should().Equal(shared ? "IncompatibleModel" : "ModelMissing");
        h.Request.Should().BeNull();
        h.Unloads.Should().Be(0);
    }

    /// <summary>Gate and inference failures preserve measured time and do not become ordinary uncertain choices.</summary>
    [Fact]
    public async Task TechnicalFailurePreservesTiming()
    {
        using var h = new Harness();
        h.Handler = _ => throw new LocalDecisionException("GateWaitTimeout", true, 12, 0);
        var result = await h.Evaluate();
        result.Status.Should().Be("unavailable");
        result.Reasons.Should().Equal("GateWaitTimeout");
        result.Timing.GateWaitMs.Should().Be(12);
        h.Unloads.Should().Be(1);
    }

    /// <summary>Caller cancellation propagates and cleanup uses an independent token after releasing ownership.</summary>
    [Fact]
    public async Task CancellationPropagatesAndCleansUp()
    {
        using var h = new Harness();
        using var canceled = new CancellationTokenSource();
        h.Handler = token => { canceled.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(new LocalDecisionResponse(Payload(), 0, 0, false)); };
        await FluentActions.Awaiting(() => h.Evaluate(cancellationToken: canceled.Token)).Should().ThrowAsync<OperationCanceledException>();
        h.Unloads.Should().Be(1);
        h.CleanupCanceled.Should().BeFalse();
    }

    /// <summary>The operation deadline remains distinct from caller cancellation and cannot fabricate scores.</summary>
    [Fact]
    public async Task DeadlineReturnsUnavailableAndCleansUp()
    {
        var clock = new DeadlineClock();
        using var h = new Harness(clock: clock);
        h.Handler = token => { clock.Expire(); return Task.FromCanceled<LocalDecisionResponse>(token); };
        var result = await h.Evaluate();
        result.Status.Should().Be("unavailable");
        result.Reasons.Should().Equal("OperationDeadline");
        result.Probabilities.Should().BeNull();
        h.Unloads.Should().Be(1);
    }

    /// <summary>Generic cleanup cannot unload a model owned by another active mail or generic operation.</summary>
    [Fact]
    public async Task AnotherOwnerPreventsCleanup()
    {
        using var h = new Harness();
        using var otherOwner = h.Residency.BeginOperation("http://localhost:11434", "tev1:latest");
        (await h.Evaluate()).Status.Should().Be("accepted");
        h.Unloads.Should().Be(0);
        otherOwner.Dispose();
        await h.Client.Object.UnloadAsync("http://localhost:11434", "tev1:latest", default);
        h.Unloads.Should().Be(1);
    }

    /// <summary>Embedding handoff uses canonical native settings whether the generic or Gmail caller is enabled.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task EmbeddingsReleaseCanonicalDecisionModel(bool genericEnabled, bool mailEnabled)
    {
        using var h = new Harness();
        var released = false;
        h.Client.Setup(c => c.ReleaseForEmbeddingAsync(It.IsAny<string>(), "canonical:latest", It.IsAny<CancellationToken>()))
            .Returns(() => { released = true; return Task.CompletedTask; });
        h.Identities = new Dictionary<string, string> { ["canonical:latest"] = "native", ["tev1:latest"] = "legacy" };
        var generator = new EmbeddingGenerator(() => released.Should().BeTrue());
        var factory = Mock.Of<IKnowledgeEmbeddingGeneratorFactory>(f => f.Create(It.IsAny<KnowledgeOptions>()) == generator);
        var service = new KnowledgeEmbeddingService(new MutableOptionsMonitor<KnowledgeOptions>(new()), factory,
            new MutableOptionsMonitor<DecisionOptions>(new() { Enabled = genericEnabled, DecisionModel = "canonical:latest" }),
            new MutableOptionsMonitor<OllamaOptions>(new()), h.Client.Object, h.Residency,
            gmailDecisionOptions: new MutableOptionsMonitor<GmailDecisionOptions>(new() { Enabled = mailEnabled }));
        var vectors = await service.GenerateAsync(["explicit input"], default);
        vectors[0].Should().Equal(0.6f, 0.8f);
        released.Should().BeTrue();
    }

    /// <summary>Feature disablement remains an authoring error and agent/taskless surfaces remain unauthorized.</summary>
    [Fact]
    public async Task DisabledAndWrongSurfaceDoNotCallInference()
    {
        using var h = new Harness(new());
        await FluentActions.Awaiting(() => h.Evaluate()).Should().ThrowAsync<WorkflowValidationException>();
        foreach (var context in new[] { ToolExecutionContext.Taskless, new ToolExecutionContext("task", [], []) })
        {
            var tool = new DecisionTool(context, h.Service);
            await FluentActions.Awaiting(() => tool.EvaluateAsync("source", "question", Choices)).Should().ThrowAsync<WorkflowValidationException>();
        }
        h.Request.Should().BeNull();
    }

    /// <summary>The existing explicit workflow-test surface retains task-bound native authorization.</summary>
    [Fact]
    public async Task AuthorizedWorkflowTestCanEvaluate()
    {
        using var h = new Harness();
        var tool = new DecisionTool(new ToolExecutionContext("task", [], [], surface: ToolInvocationSurface.WorkflowTest), h.Service);
        var result = Json(await tool.EvaluateAsync("Explicit test text.", "Is the request complete?", Choices));
        result.GetProperty("status").GetString().Should().Be("accepted");
        result.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    }

    /// <summary>The common scoring service retains named Boolean and rubric question support independently of mail policy.</summary>
    [Fact]
    public async Task CommonScoringRetainsAdvancedNativeQuestionTypes()
    {
        using var h = new Harness(new());
        var questions = DecisionQuestionSet.Parse(Json("""{"greeting":{"type":"noul","instructions":"Is there a greeting?"},"quality":{"type":"score","instructions":"Rate completeness.","criteria":["Incomplete","Complete"]}}"""));
        h.Response = Json("""{"answers":{"greeting":{"type":"noul","noul":0.9},"quality":{"type":"score","score":0.8,"probabilities":{"0":0.2,"1":0.8},"confidence":0.6}}}""");
        var response = await h.Service.ScoreAsync(new("http://localhost:11434", "tev1:latest", "Hello.", questions.Json, 15, 30, 10), 120, questions, default);
        var typed = questions.ValidateNative(response.Payload, 50);
        typed.Answers["greeting"].Should().Be(true);
        typed.Answers["quality"].Should().Be(1);
        typed.Scores["quality"].Score.Should().Be(0.8);
        h.Unloads.Should().Be(0);
    }

    /// <summary>The actual registry rejects missing group grants and agent exposure before activating a native caller.</summary>
    [Fact]
    public async Task RegistryEnforcesWorkflowPermissions()
    {
        var registry = new ToolRegistry(ToolCatalog.Default, Mock.Of<IToolTargetActivator>());
        var context = new ToolExecutionContext("task", [], [], surface: ToolInvocationSurface.Workflow);
        var args = Json("""{"source":"s","question":"q","options":{"a":"A","b":"B"}}""");
        await FluentActions.Awaiting(() => registry.InvokeAsync("decision.evaluate", args, [], context, default))
            .Should().ThrowAsync<WorkflowValidationException>();
        registry.GetAgentTools(["decision"], context).Should().BeEmpty();
        FluentActions.Invoking(() => registry.GetAgentToolsByAliases(["decision.evaluate"], ["decision"], context))
            .Should().Throw<WorkflowValidationException>();
    }

    /// <summary>The shipped forward-only workflow handles each status before routing accepted choices.</summary>
    [Theory]
    [InlineData(0.9, false, "process")]
    [InlineData(0.9, true, "clarify")]
    [InlineData(0.5, false, "uncertain")]
    [InlineData(-1, false, "unavailable")]
    public async Task ShippedWorkflowRoutesThroughActualRegistryAndEngine(double top, bool clarify, string branch)
    {
        using var h = new Harness();
        h.Response = top < 0 ? Json("{}") : clarify ? Json("""{"answers":{"decision":{"type":"choice","choice":"clarify","probabilities":{"process":0.1,"clarify":0.9},"confidence":0.8}}}""") : Payload(top);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OD.MyLocalAgent.sln"))) root = root.Parent;
        var task = ScheduledTaskSerializer.Deserialize(File.ReadAllText(Path.Combine(root!.FullName, "Docs", "samples", "decision-readiness.json")));
        task.Enabled.Should().BeFalse();
        using var provider = new ServiceCollection().AddSingleton<IDecisionEvaluationService>(h.Service).BuildServiceProvider();
        var registry = new ToolRegistry(ToolCatalog.Default, new ToolTargetActivator(provider, ToolCatalog.Default));
        var engine = new SequentialWorkflowEngine(registry, [new EchoSkill()], Mock.Of<IMyLocalAgentService>(),
            TimeProvider.System, NullLogger<SequentialWorkflowEngine>.Instance);
        var result = await engine.ExecuteAsync(task, default);
        result.Success.Should().BeTrue();
        result.FinalText.Should().Contain("Handled " + branch);
        h.Client.Verify(c => c.DecideAsync(It.IsAny<LocalDecisionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class EchoSkill : ISkill
    {
        public string Name => "summarize";
        public string AgentFunctionName => "test_echo";
        public string Description => "Returns an explicit test branch marker without model inference.";
        public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken) => Task.FromResult(input);
    }

    private sealed class EmbeddingGenerator(Action beforeGenerate) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            beforeGenerate();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(_ => new Embedding<float>(new float[] { 3, 4 }))));
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class DeadlineClock : TimeProvider
    {
        private readonly List<DeadlineTimer> _timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new DeadlineTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }
        public void Expire() { foreach (var timer in _timers.ToArray()) timer.Fire(); }
        private sealed class DeadlineTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "general-decision-" + Guid.NewGuid().ToString("N"));
        public Mock<ILocalDecisionClient> Client { get; } = new(MockBehavior.Strict);
        public JsonElement Response { get; set; } = Payload();
        public IReadOnlyDictionary<string, string> Identities { get; set; } = new Dictionary<string, string>
        { ["tev1:latest"] = "decision", ["gemma4:12b"] = "chat", ["embeddinggemma:300m"] = "embedding" };
        public LocalDecisionRequest? Request { get; private set; }
        public Func<CancellationToken, Task<LocalDecisionResponse>>? Handler { get; set; }
        public int Unloads { get; private set; }
        public bool CleanupCanceled { get; private set; }
        public DecisionEvaluationService Service { get; }
        public DecisionModelResidencyCoordinator Residency { get; }
        public Harness(DecisionOptions? settings = null, TimeProvider? clock = null)
        {
            Residency = new DecisionModelResidencyCoordinator(_directory, TimeProvider.System);
            Client.Setup(c => c.GetModelIdentitiesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(Identities));
            Client.Setup(c => c.DecideAsync(It.IsAny<LocalDecisionRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((LocalDecisionRequest request, int _, CancellationToken token) =>
                { Request = request; return Handler?.Invoke(token) ?? Task.FromResult(new LocalDecisionResponse(Response, 2, 3, true)); });
            Client.Setup(c => c.UnloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string endpoint, string model, CancellationToken token) => Residency.CleanupAsync(endpoint, model,
                    () => { Unloads++; CleanupCanceled = token.IsCancellationRequested; return Task.CompletedTask; }, token));
            Service = new(new MutableOptionsMonitor<DecisionOptions>(settings ?? new() { Enabled = true }),
                new MutableOptionsMonitor<OllamaOptions>(new()), new MutableOptionsMonitor<KnowledgeOptions>(new()),
                Client.Object,
                Residency, clock ?? TimeProvider.System, NullLogger<DecisionEvaluationService>.Instance);
        }
        public Task<DecisionEvaluationResult> Evaluate(string source = "The request includes a destination and delivery date.",
            JsonElement? acceptance = null, CancellationToken cancellationToken = default) =>
            Service.EvaluateAsync(source, "Is this request complete?", Choices, acceptance, cancellationToken);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
}
