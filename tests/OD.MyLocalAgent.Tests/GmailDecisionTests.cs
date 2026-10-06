using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.Workflows;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies local decision policy, workflow authorization, bounded coverage, and deterministic reports.</summary>
public sealed class GmailDecisionTests
{
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static DecisionQuestionSet Questions() => DecisionQuestionSet.Parse(Json(GmailDecisionDefaults.QuestionsJson));
    private static DecisionAcceptancePolicy Policy(string acceptance = "{}", string arbitration = "{}") =>
        DecisionAcceptancePolicy.Parse(Questions(), new GmailDecisionOptions(), Json(acceptance), Json(GmailDecisionDefaults.ReportMappingJson), Json(arbitration));

    private static JsonElement Native(string category = "information", double top = 0.95, double noul = 0.05)
    {
        var keys = new[] { "urgent", "action", "information", "noise", "uncertain" };
        var probabilities = keys.ToDictionary(k => k, k => k == category ? top : (1 - top) / 4);
        return JsonSerializer.SerializeToElement(new
        {
            answers = new
            {
                category = new { type = "choice", choice = category, probabilities, confidence = 0.9 },
                reply = new { type = "noul", noul },
                priority = new { type = "score", score = 1.0, probabilities = new Dictionary<string, double> { ["0"] = 0.05, ["1"] = 0.9, ["2"] = 0.05 }, confidence = 0.8 },
            },
        });
    }

    /// <summary>Missing settings remain disabled; all defaults satisfy validation.</summary>
    [Fact]
    public void DefaultsAreOptInAndBounded()
    {
        var o = new GmailDecisionOptions();
        o.Enabled.Should().BeFalse();
        GmailDecisionOptions.IsValid(o).Should().BeTrue();
        o.DecisionModel.Should().Be("tev1:latest");
        o.BatchTimeoutSeconds.Should().Be(180);
        GmailDecisionOptions.IsValid(o with { KeepAliveSeconds = 61 }).Should().BeFalse();
        GmailDecisionOptions.IsValid(o with { UnloadAfterBatch = false }).Should().BeFalse();
        GmailDecisionOptions.IsValid(o with { MinNoulCertaintyPercent = 49 }).Should().BeFalse();
    }

    /// <summary>Endpoint validation uses URI/IP identities and rejects cloud, userinfo, paths and forged hosts.</summary>
    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("https://127.0.0.1:443", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://localhost.example.test", false)]
    [InlineData("http://example.test/localhost", false)]
    [InlineData("http://user@localhost:11434", false)]
    [InlineData("http://192.168.1.2:11434", false)]
    [InlineData("http://127.0.0.1:11434/path", false)]
    public void EndpointsAreStrictlyLocal(string endpoint, bool valid) => LocalDecisionEndpoint.IsValid(endpoint).Should().Be(valid);

    /// <summary>Questions preserve rubric order and reject duplicate/colliding identifiers.</summary>
    [Theory]
    [InlineData("{\"Category\":{\"type\":\"noul\",\"instructions\":\"A\"},\"category\":{\"type\":\"noul\",\"instructions\":\"B\"}}")]
    [InlineData("{\"category\":{\"type\":\"choice\",\"instructions\":\"A\",\"criteria\":{\"a\":\"A\",\"A\":\"B\"}}}")]
    [InlineData("{\"{{task.prompt}}\":{\"type\":\"noul\",\"instructions\":\"A\"}}")]
    [InlineData("{\"q\":{\"type\":\"noul\",\"instructions\":\"A\",\"criteria\":{\"true\":\"Yes\"}}}")]
    [InlineData("{\"q\":{\"type\":\"score\",\"instructions\":\"A\",\"criteria\":[\"One\"]}}")]
    [InlineData("{\"q\":{\"type\":\"noul\",\"instructions\":\"A\",\"tools\":[]}}")]
    public void InvalidQuestionsFailBeforeBinding(string questions) =>
        FluentActions.Invoking(() => DecisionQuestionSet.Parse(Json(questions))).Should().Throw<Exception>();

    /// <summary>Native scores remain native; certainty and rubric levels use different mathematics.</summary>
    [Fact]
    public void NativeTypesAndAcceptanceUseUnroundedScores()
    {
        var scores = Questions().ValidateNative(Native("information", 0.8, 0.2), 50);
        scores.Answers["reply"].Should().Be(false);
        scores.Answers["priority"].Should().Be(1);
        scores.Scores["priority"].Score.Should().Be(1);
        Policy().Evaluate(scores, false).Should().BeEmpty();
        Policy().Evaluate(Questions().ValidateNative(Native("information", 0.79999), 50), false)
            .Should().Contain("TopProbabilityBelowThreshold");
        Policy().Evaluate(Questions().ValidateNative(Native("information", 0.95, 0.5), 50), false)
            .Should().Contain("NoulCertaintyBelowThreshold");
    }

    /// <summary>Per-choice policy wins over question/workflow defaults without inventing probabilities.</summary>
    [Fact]
    public void PerChoicePrecedenceAndAlwaysArbitrateAreExplicit()
    {
        var p = Policy(GmailDecisionDefaults.AcceptanceJson);
        p.Evaluate(Questions().ValidateNative(Native("noise", 0.85), 50), false).Should().Contain("TopProbabilityBelowThreshold");
        p.Evaluate(Questions().ValidateNative(Native("information", 0.85), 50), false).Should().BeEmpty();
        p.Evaluate(Questions().ValidateNative(Native("uncertain"), 50), false).Should().Contain("AlwaysArbitrateChoice");
        p.Evaluate(Questions().ValidateNative(Native(), 50), true).Should().Contain("CurrentContentTruncated");
    }

    /// <summary>Typed cross-question conflicts compare rubric-level probability rather than weighted score.</summary>
    [Fact]
    public void ConflictRulesAndBudgetAreDeterministic()
    {
        var p = Policy(arbitration: """
            {"conflicts":[{"choiceQuestion":"category","choice":"noise","noulQuestion":"reply","noulAtLeastPercent":70}]}
            """);
        p.Evaluate(Questions().ValidateNative(Native("noise", 0.95, 0.8), 50), false).Should().Contain("CrossQuestionConflict");
        DecisionAcceptancePolicy.ArbitrationCap(37, 20, 0).Should().Be(7);
        DecisionAcceptancePolicy.ArbitrationCap(37, 100, 3).Should().Be(3);
        DecisionAcceptancePolicy.ArbitrationCap(37, 0, 0).Should().Be(0);
        FluentActions.Invoking(() => Policy("{\"perQuestion\":{\"reply\":{\"minTopProbabilityPercent\":90}}}"))
            .Should().Throw<WorkflowValidationException>();
    }

    /// <summary>Malformed distributions and selected/top mismatches are not normalized into success.</summary>
    [Theory]
    [InlineData("{\"choice\":\"a\",\"probabilities\":{\"a\":0.8,\"b\":0.1},\"type\":\"choice\"}")]
    [InlineData("{\"choice\":\"b\",\"probabilities\":{\"a\":0.8,\"b\":0.2},\"type\":\"choice\"}")]
    [InlineData("{\"choice\":\"a\",\"probabilities\":{\"a\":1.1,\"b\":-0.1},\"type\":\"choice\"}")]
    [InlineData("{\"choice\":\"a\",\"probabilities\":{\"a\":0.8,\"c\":0.2},\"type\":\"choice\"}")]
    public void MalformedNativeScoresAreRejected(string answer)
    {
        var set = DecisionQuestionSet.Parse(Json("{\"q\":{\"type\":\"choice\",\"instructions\":\"Choose.\",\"criteria\":{\"a\":\"A\",\"b\":\"B\"}}}"));
        FluentActions.Invoking(() => set.ValidateNative(Json("{\"answers\":{\"q\":" + answer + "}}"), 50)).Should().Throw<Exception>();
    }

    /// <summary>Ties trigger arbitration, even when percentage thresholds are explicitly zero.</summary>
    [Fact]
    public void TiedScoresAreAmbiguous()
    {
        var set = Questions();
        var native = Native("information", 0.2);
        Policy("{\"minTopProbabilityPercent\":0,\"minMarginPercentagePoints\":0}")
            .Evaluate(set.ValidateNative(native, 50), false).Should().Contain("TiedTopScores");
    }

    /// <summary>Workflow-only tools are absent from agent discovery and rejected before target activation.</summary>
    [Theory]
    [InlineData(ToolInvocationSurface.Agent, "task", false)]
    [InlineData(ToolInvocationSurface.Workflow, null, false)]
    [InlineData(ToolInvocationSurface.Workflow, "task", true)]
    [InlineData(ToolInvocationSurface.WorkflowTest, "task", true)]
    public async Task InvocationSurfaceIsEnforced(ToolInvocationSurface surface, string? taskId, bool allowed)
    {
        using var harness = new Harness();
        var activator = new Mock<IToolTargetActivator>();
        activator.Setup(a => a.CreateTarget(typeof(GmailDecisionTool), It.IsAny<ToolExecutionContext>())).Returns(harness.Tool);
        var registry = new ToolRegistry(ToolCatalog.Default, activator.Object);
        registry.GetAgentTools(["gmailDecision"]).Should().BeEmpty();
        var context = new ToolExecutionContext(taskId, [], [], surface: surface);
        var args = Json("{\"source\":\"{}\"}");
        if (!allowed)
        {
            await FluentActions.Awaiting(() => registry.InvokeAsync("gmailDecision.renderReport", args, ["gmailDecision"], context, default))
                .Should().ThrowAsync<WorkflowValidationException>().WithMessage("*authorized workflow*");
            activator.Verify(a => a.CreateTarget(It.IsAny<Type>(), It.IsAny<ToolExecutionContext>()), Times.Never);
        }
        else
        {
            var read = await harness.Read();
            var report = await registry.InvokeAsync("gmailDecision.renderReport", JsonSerializer.SerializeToElement(new { source = read }), ["gmailDecision"], context, default);
            report.Should().Contain("Relevant information").And.Contain("Subject");
        }
        context.WithKnowledgeScope([]).Surface.Should().Be(surface);
        await FluentActions.Awaiting(() => registry.InvokeAsync("gmailDecision.readClassify", default, ["gmail"], context, default))
            .Should().ThrowAsync<WorkflowValidationException>();
    }

    /// <summary>Missing decision model activates one lightweight call per already collected message.</summary>
    [Fact]
    public async Task MissingModelFallsBackWithoutRefetchOrArbitration()
    {
        using var h = new Harness(3);
        h.Client.Identities = new Dictionary<string, string>();
        var output = Json(await h.Read());
        output.GetProperty("counts").GetProperty("collected").GetInt32().Should().Be(3);
        output.GetProperty("counts").GetProperty("lightweightFallbackResolved").GetInt32().Should().Be(3);
        h.Client.DecisionCalls.Should().Be(0);
        h.Client.LlmCalls.Should().Be(3);
        h.Connector.Verify(c => c.GetDecisionBatchAsync("Recent", 1, 100, It.IsAny<CancellationToken>()), Times.Once);
        output.GetRawText().Should().NotContain("SECRET MAIL BODY");
        output.GetProperty("status").GetString().Should().Be("CompletedWithWarnings");
    }

    /// <summary>Two consecutive invalid decisions open a batch circuit without repeated probes.</summary>
    [Fact]
    public async Task InvalidResponseCircuitIsOperationScoped()
    {
        using var h = new Harness(4);
        h.Client.NativePayload = Json("{\"answers\":{}}");
        var result = Json(await h.Read());
        h.Client.DecisionCalls.Should().Be(2);
        h.Client.LlmCalls.Should().Be(4);
        result.GetProperty("counts").GetProperty("classified").GetInt32().Should().Be(4);
        h.Client.UnloadCalls.Should().Be(1);
    }

    /// <summary>Transient retry is bounded to one and does not cause redundant arbitration after success.</summary>
    [Fact]
    public async Task TransientRetryIsBounded()
    {
        using var h = new Harness();
        h.Client.DecisionHandler = (_, _, _) => h.Client.DecisionCalls == 1
            ? throw new LocalDecisionException("ProviderOverload", true)
            : Task.FromResult(new LocalDecisionResponse(Native(), 1, 2, true));
        var result = Json(await h.Read());
        h.Client.DecisionCalls.Should().Be(2);
        h.Client.LlmCalls.Should().Be(0);
        result.GetProperty("messages")[0].GetProperty("status").GetString().Should().Be("Classified");
    }

    /// <summary>Independent arbitration cannot hide a client exchange that the decision model kept visible.</summary>
    [Fact]
    public async Task ArbitrationProtectsVisibilityAndRemainsIndependent()
    {
        using var h = new Harness();
        h.Client.NativePayload = Native("information", 0.6);
        h.Client.LlmPayload = Json("{\"category\":\"noise\",\"reply\":true,\"priority\":2}");
        var result = Json(await h.Read());
        var row = result.GetProperty("messages")[0];
        row.GetProperty("category").GetString().Should().Be("information");
        row.GetProperty("answers").GetProperty("reply").GetBoolean().Should().BeTrue();
        row.GetProperty("answers").GetProperty("priority").GetInt32().Should().Be(2);
        row.GetProperty("disagreement").GetBoolean().Should().BeTrue();
        row.GetProperty("decisionSource").GetString().Should().Be("arbitrated");
        h.Client.LastLlmRequest!.Source.Should().NotContain("probabilities").And.NotContain("SECRET TASK PROMPT");
        h.Client.LlmCalls.Should().Be(1);
    }

    /// <summary>Exhausted arbitration retains valid native answers provisionally with complete coverage.</summary>
    [Fact]
    public async Task ArbitrationCapPreservesProvisionalRows()
    {
        using var h = new Harness(3);
        h.Client.NativePayload = Native("information", 0.6);
        var result = Json(await h.Read(arbitration: Json("{\"maxPercent\":34}")));
        h.Client.LlmCalls.Should().Be(1);
        result.GetProperty("counts").GetProperty("provisional").GetInt32().Should().Be(2);
        result.GetProperty("counts").GetProperty("classified").GetInt32().Should().Be(1);
        GmailDecisionReportRenderer.Render(result.GetRawText()).Should().Contain("Human review: Provisional");
    }

    /// <summary>Cancellation performs independent cleanup and never invokes fallback or publishes output.</summary>
    [Fact]
    public async Task CancellationCleansUpWithoutFallback()
    {
        using var h = new Harness();
        using var canceled = new CancellationTokenSource();
        h.Client.DecisionHandler = (_, _, _) =>
        {
            canceled.Cancel();
            throw new OperationCanceledException(canceled.Token);
        };
        await FluentActions.Awaiting(() => h.Read(cancellationToken: canceled.Token)).Should().ThrowAsync<OperationCanceledException>();
        h.Client.LlmCalls.Should().Be(0);
        h.Client.UnloadCalls.Should().Be(1);
        h.Client.CleanupWasCanceled.Should().BeFalse();
    }

    /// <summary>Shared installed identities are rejected before collection and never unloaded.</summary>
    [Fact]
    public async Task SharedModelAliasCannotBeUnloaded()
    {
        using var h = new Harness();
        h.Client.Identities = new Dictionary<string, string> { ["tev1:latest"] = "same", ["gemma4:12b"] = "same" };
        await FluentActions.Awaiting(() => h.Read()).Should().ThrowAsync<WorkflowValidationException>().WithMessage("*shares*identity*");
        h.Client.UnloadCalls.Should().Be(0);
        h.Connector.Verify(c => c.GetDecisionBatchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Empty batches explicitly render no messages; cleanup failures become visible warnings.</summary>
    [Fact]
    public async Task EmptyReportAndCleanupWarningsAreVisible()
    {
        using var h = new Harness(0);
        h.Client.FailCleanup = true;
        var output = await h.Read();
        GmailDecisionReportRenderer.Render(output).Should().Contain("No messages found").And.Contain("MemoryCleanupFailure");
        h.Client.DecisionCalls.Should().Be(0);
    }

    /// <summary>Display text cannot inject HTML, Markdown links or new report sections; hidden noise is accounted for.</summary>
    [Fact]
    public async Task ReportEscapesDisplayDataAndExplainsHiddenRows()
    {
        using var h = new Harness();
        h.Mails[0] = h.Mails[0] with { Subject = "<script>[click](https://evil.test)\n# forged" };
        h.Client.NativePayload = Native("noise");
        var output = await h.Read();
        GmailDecisionReportRenderer.Render(output).Should().NotContain("<script>").And.NotContain("[click]").And.NotContain("\n# forged");
        var hidden = GmailDecisionReportRenderer.Render(output, includeNoise: false);
        hidden.Should().Contain("1 newsletters/advertising rows are intentionally hidden");
        var modified = output.Replace("\"collected\":1", "\"collected\":2", StringComparison.Ordinal);
        FluentActions.Invoking(() => GmailDecisionReportRenderer.Render(modified)).Should().Throw<WorkflowValidationException>();
    }

    /// <summary>Projection uses conservative UTF-8 budgeting and keeps Unicode valid.</summary>
    [Fact]
    public void ProjectionBoundsUnicodeAndSeparatesHistory()
    {
        var mail = new EmailMessageDto("id", "sender", "to", "Subject", "", string.Concat(Enumerable.Repeat("é漢😀", 2000)), DateTimeOffset.UtcNow, false);
        var projection = GmailDecisionProjection.Create(mail, 4000, GmailDecisionProjection.MailBudget(Questions()));
        projection.CurrentContentTruncated.Should().BeTrue();
        projection.TokenEstimate.Should().BeLessThanOrEqualTo(GmailDecisionProjection.MailBudget(Questions()));
        projection.DecisionSource.Should().Contain("Current content omitted");
        System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(projection.DecisionSource)).Should().Be(projection.DecisionSource);
    }

    /// <summary>Authored output placeholders cannot turn a mail into a mutable policy.</summary>
    [Fact]
    public void PolicyPlaceholdersAndUnknownArgumentsAreRejected()
    {
        var args = JsonSerializer.SerializeToElement(new { questions = Json(GmailDecisionDefaults.QuestionsJson), reportMapping = Json(GmailDecisionDefaults.ReportMappingJson), selection = "{{task.prompt}}" });
        FluentActions.Invoking(() => GmailDecisionContractValidator.Validate(args)).Should().Throw<WorkflowValidationException>();
        var q = GmailDecisionDefaults.QuestionsJson.Replace("Classify professional mail.", "{{prior.output}}", StringComparison.Ordinal);
        args = JsonSerializer.SerializeToElement(new { questions = Json(q), reportMapping = Json(GmailDecisionDefaults.ReportMappingJson) });
        FluentActions.Invoking(() => GmailDecisionContractValidator.Validate(args)).Should().Throw<WorkflowValidationException>();
    }

    /// <summary>Disabled readers and Fail policy never silently activate a fallback pipeline.</summary>
    [Fact]
    public async Task DisabledAndFailPoliciesRejectBeforeFallback()
    {
        using var disabled = new Harness(settings: new() { Enabled = false });
        await FluentActions.Awaiting(() => disabled.Read()).Should().ThrowAsync<WorkflowValidationException>();
        disabled.Client.LlmCalls.Should().Be(0);
        disabled.Connector.Verify(c => c.GetDecisionBatchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        using var fail = new Harness(settings: new() { Enabled = true, UnavailableBehavior = "Fail" });
        fail.Client.Identities = new Dictionary<string, string>();
        await FluentActions.Awaiting(() => fail.Read()).Should().ThrowAsync<LocalDecisionException>();
        fail.Client.LlmCalls.Should().Be(0);
    }

    /// <summary>Operation deadlines retain all collected rows, with completed and skipped coverage distinguished.</summary>
    [Fact]
    public async Task DeadlinePreservesCompletedAndSkippedRows()
    {
        var clock = new DeadlineClock();
        using var h = new Harness(3, clock: clock);
        h.Client.DecisionHandler = (_, _, ct) =>
        {
            if (h.Client.DecisionCalls == 2) { clock.Expire(); throw new OperationCanceledException(ct); }
            return Task.FromResult(new LocalDecisionResponse(Native(), 0, 1, false));
        };
        var result = Json(await h.Read());
        result.GetProperty("status").GetString().Should().Be("Partial");
        result.GetProperty("counts").GetProperty("collected").GetInt32().Should().Be(3);
        result.GetProperty("counts").GetProperty("classified").GetInt32().Should().Be(1);
        result.GetProperty("counts").GetProperty("unresolved").GetInt32().Should().Be(2);
        result.GetProperty("messages")[2].GetProperty("arbitrationReasons")[0].GetString().Should().Be("NotProcessedBeforeDeadline");
        h.Client.LlmCalls.Should().Be(0);
        GmailDecisionReportRenderer.Render(result.GetRawText()).Should().Contain("Unresolved (2)");
    }

    /// <summary>A settings change during inference cannot alter the snapshotted policy or model names.</summary>
    [Fact]
    public async Task SettingsAreSnapshottedAndLogsContainNoMailText()
    {
        using var h = new Harness(2);
        h.Client.DecisionHandler = (_, _, _) =>
        {
            h.Options.Set(new() { Enabled = true, MinTopProbabilityPercent = 100, DecisionModel = "other:latest" });
            return Task.FromResult(new LocalDecisionResponse(Native(), 0, 1, false));
        };
        var output = Json(await h.Read());
        output.GetProperty("counts").GetProperty("decisionAccepted").GetInt32().Should().Be(2);
        output.GetProperty("effectiveModels").GetProperty("decision").GetString().Should().Be("tev1:latest");
        string.Join("\n", h.Log.Messages).Should().NotContain("SECRET MAIL BODY").And.NotContain("sender@example.test").And.NotContain("Subject 0");
    }

    /// <summary>Probability details are omitted before output-size enforcement, never message rows.</summary>
    [Fact]
    public void OutputSizeReductionPreservesEveryRow()
    {
        var result = new GmailDecisionResult
        {
            Messages = Enumerable.Range(0, 200).Select(i => new DecisionMailResult
            {
                Id = "id" + i,
                DecisionScores = Enumerable.Range(0, 8).ToDictionary(q => "question" + q, _ => new NativeDecisionScore
                {
                    Choice = "answer0", Top = 0.9, Margin = 0.8,
                    Probabilities = Enumerable.Range(0, 24).ToDictionary(a => "answer" + a + new string('x', 40), a => a == 0 ? 0.9 : 0.1 / 23),
                }),
            }).ToList(),
        };
        result.Counts = GmailDecisionTool.Count(result.Messages);
        var output = Json(GmailDecisionTool.SerializeBounded(result));
        output.GetProperty("scoreDetailsOmitted").GetBoolean().Should().BeTrue();
        output.GetProperty("messages").GetArrayLength().Should().Be(200);
        System.Text.Encoding.UTF8.GetByteCount(output.GetRawText()).Should().BeLessThanOrEqualTo(512 * 1024);
    }

    /// <summary>Actual workflow execution passes an explicit surface and propagates warnings without any agent request.</summary>
    [Fact]
    public async Task WorkflowIntegrationUsesLocalToolsAndSurfacesWarnings()
    {
        using var h = new Harness();
        h.Client.Identities = new Dictionary<string, string>();
        var activator = new Mock<IToolTargetActivator>();
        activator.Setup(a => a.CreateTarget(typeof(GmailDecisionTool), It.IsAny<ToolExecutionContext>()))
            .Returns((Type _, ToolExecutionContext c) =>
            {
                c.Surface.Should().Be(ToolInvocationSurface.Workflow);
                c.StepId.Should().NotBeNullOrWhiteSpace();
                c.RunId.Should().NotBeNullOrWhiteSpace();
                return h.Tool;
            });
        var registry = new ToolRegistry(ToolCatalog.Default, activator.Object);
        var engine = new SequentialWorkflowEngine(registry, [], new Mock<IMyLocalAgentService>(MockBehavior.Strict).Object,
            TimeProvider.System, NullLogger<SequentialWorkflowEngine>.Instance);
        var task = new ScheduledTask
        {
            Id = "decision-workflow", Name = "Decision", Schedule = "0 7 * * *", Tools = ["gmailDecision"],
            Workflow =
            [
                new() { Step = "gmailDecision.readClassify", Id = "triage", Args = JsonSerializer.SerializeToElement(new { questions = Json(GmailDecisionDefaults.QuestionsJson), reportMapping = Json(GmailDecisionDefaults.ReportMappingJson) }) },
                new() { Step = "gmailDecision.renderReport", Id = "report", Args = Json("{\"source\":\"{{triage.output}}\"}") },
            ],
        };
        var result = await engine.ExecuteAsync(task, default);
        result.Success.Should().BeTrue("workflow errors: {0}", result.ErrorMessage + string.Join("; ", result.Steps.Select(s => s.Error?.ToString() ?? s.ErrorMessage)));
        result.FinalText.Should().Contain("Relevant information").And.Contain("Subject 0");
        result.Warnings.Should().Contain(w => w.Code.StartsWith("GmailDecision.", StringComparison.Ordinal));
        h.Client.LlmCalls.Should().Be(1);
        h.Connector.Verify(c => c.GetDecisionBatchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "gmail-decision-tests-" + Guid.NewGuid().ToString("N"));
        public FakeClient Client { get; } = new();
        public Mock<IGmailConnector> Connector { get; } = new();
        public List<EmailMessageDto> Mails { get; }
        public GmailDecisionTool Tool { get; }
        public MutableOptionsMonitor<GmailDecisionOptions> Options { get; }
        public RecordingLogger<GmailDecisionTool> Log { get; } = new();
        public Harness(int count = 1, GmailDecisionOptions? settings = null, TimeProvider? clock = null)
        {
            Options = new(settings ?? new() { Enabled = true });
            Mails = Enumerable.Range(0, count).Select(i => new EmailMessageDto("opaque-" + i, "sender@example.test", "to", "Subject " + i,
                "", "SECRET MAIL BODY", DateTimeOffset.UtcNow, false)).ToList();
            Connector.Setup(c => c.GetDecisionBatchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult(new GmailReadBatch(Mails, false)));
            Tool = new GmailDecisionTool(new ToolExecutionContext("test", [], [], surface: ToolInvocationSurface.Workflow), Connector.Object,
                Options, new MutableOptionsMonitor<OllamaOptions>(new()),
                new MutableOptionsMonitor<KnowledgeOptions>(new()), new MutableOptionsMonitor<GuardrailsOptions>(new()), Client,
                new LightweightMailClassifier(Client), new DecisionModelResidencyCoordinator(_directory, TimeProvider.System),
                clock ?? TimeProvider.System, Log);
        }
        public Task<string> Read(JsonElement arbitration = default, CancellationToken cancellationToken = default) =>
            Tool.ReadClassifyAsync(Json(GmailDecisionDefaults.QuestionsJson), Json(GmailDecisionDefaults.ReportMappingJson), arbitration: arbitration, cancellationToken: cancellationToken);
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class DeadlineClock : TimeProvider
    {
        private readonly List<TestTimer> _timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TestTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }
        public void Expire() { foreach (var timer in _timers.ToArray()) timer.Fire(); }
        private sealed class TestTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class FakeClient : ILocalDecisionClient
    {
        public IReadOnlyDictionary<string, string> Identities { get; set; } = new Dictionary<string, string> { ["tev1:latest"] = "decision", ["gemma4:12b"] = "chat" };
        public JsonElement NativePayload { get; set; } = Native();
        public JsonElement LlmPayload { get; set; } = Json("{\"category\":\"information\",\"reply\":false,\"priority\":1}");
        public int DecisionCalls { get; private set; }
        public int LlmCalls { get; private set; }
        public int UnloadCalls { get; private set; }
        public bool CleanupWasCanceled { get; private set; }
        public bool FailCleanup { get; set; }
        public LocalDecisionRequest? LastLlmRequest { get; private set; }
        public Func<LocalDecisionRequest, int, CancellationToken, Task<LocalDecisionResponse>>? DecisionHandler { get; set; }
        public Task<IReadOnlyDictionary<string, string>> GetModelIdentitiesAsync(string endpoint, CancellationToken cancellationToken) => Task.FromResult(Identities);
        public Task<LocalDecisionResponse> DecideAsync(LocalDecisionRequest request, int coldTimeoutSeconds, CancellationToken cancellationToken)
        {
            DecisionCalls++;
            return DecisionHandler?.Invoke(request, coldTimeoutSeconds, cancellationToken) ?? Task.FromResult(new LocalDecisionResponse(NativePayload, 1, 2, false));
        }
        public Task<LocalDecisionResponse> ClassifyAsync(LocalDecisionRequest request, JsonElement schema, CancellationToken cancellationToken)
        {
            LlmCalls++;
            LastLlmRequest = request;
            return Task.FromResult(new LocalDecisionResponse(LlmPayload, 1, 3, false));
        }
        public Task UnloadAsync(string endpoint, string model, CancellationToken cancellationToken)
        {
            UnloadCalls++;
            CleanupWasCanceled = cancellationToken.IsCancellationRequested;
            return FailCleanup ? Task.FromException(new LocalDecisionException("CleanupFailure")) : Task.CompletedTask;
        }
        public Task ReleaseForEmbeddingAsync(string endpoint, string model, CancellationToken cancellationToken) =>
            UnloadAsync(endpoint, model, cancellationToken);
    }
}
