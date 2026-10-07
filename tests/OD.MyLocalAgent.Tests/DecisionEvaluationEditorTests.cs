using System.Text.Json;
using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies decision authoring and explicit, isolated sample evaluation without live inference.</summary>
public sealed class DecisionEvaluationEditorTests
{
    private const string Workflow = """
        [{"step":"decision.evaluate","id":"readiness","args":{"source":"Literal draft source","question":"Is this request complete?","options":{"process":"Ready to proceed","clarify":"Essential information is missing"}}}]
        """;

    private static WorkflowStepEditorRequest Request(string workflow = Workflow, bool permitted = true) => new(
        workflow, workflow.IndexOf("decision.evaluate", StringComparison.Ordinal), WorkflowStepEditorMode.Edit,
        permitted ? ["decision"] : [], [], [], [], WorkflowErrorPolicy.StopOnError, 0.2, 1);

    private static (WorkflowStepEditorViewModel Editor, Mock<IDecisionEvaluationService> Service) Editor(
        bool permitted = true, bool enabled = true, string workflow = Workflow)
    {
        var service = new Mock<IDecisionEvaluationService>(MockBehavior.Strict);
        var editor = new WorkflowStepEditorViewModel(decisionEvaluation: service.Object,
            decisionOptions: new MutableOptionsMonitor<DecisionOptions>(new() { Enabled = enabled }));
        editor.Initialize(Request(workflow, permitted));
        return (editor, service);
    }

    private static DecisionEvaluationResult Result(string status = "accepted") => new(1, status,
        status == "unavailable" ? null : "process",
        status == "unavailable" ? null : new Dictionary<string, double> { ["process"] = .9, ["clarify"] = .1 },
        status == "unavailable" ? null : .9, status == "unavailable" ? null : .8,
        status == "unavailable" ? null : .53, new(.8, .2, 0),
        status == "accepted" ? [] : [status == "uncertain" ? "BelowTopThreshold" : "ModelMissing"],
        "OllamaSystemOne", "test-model", new(3, 12, 30, true));

    /// <summary>Opening and applying an unchanged decision keeps the original draft and runs nothing.</summary>
    [Fact]
    public void OpenAndNoOpApplyPreserveOriginal()
    {
        var (editor, service) = Editor();
        editor.IsDecisionEvaluationSelected.Should().BeTrue();
        editor.ShowGenericArguments.Should().BeFalse();
        editor.EvaluationOptions.Select(o => o.Key).Should().Equal("process", "clarify");
        editor.HasChanges.Should().BeFalse();
        editor.EvaluationDefaultsText.Should().Contain("80%").And.Contain("20 percentage points");
        editor.EvaluationEffectiveText.Should().Contain("Settings");
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().BeNull();
        editor.IsApplyCloseRequested.Should().BeTrue();
        service.VerifyNoOtherCalls();
    }

    /// <summary>Ordered option edits and percentages serialize through the canonical workflow contract.</summary>
    [Fact]
    public void FieldsPersistOrderedOptionsAndNormalizedThresholds()
    {
        var (editor, service) = Editor();
        editor.SelectedEvaluationOption = editor.EvaluationOptions[1];
        editor.MoveEvaluationOptionUpCommand.Execute(null);
        editor.EvaluationOptions[0].Description = "Précisions essentielles 漢😀";
        editor.OverrideEvaluationTop = true;
        editor.EvaluationTopPercent = "92.5";
        editor.OverrideEvaluationMargin = true;
        editor.EvaluationMarginPercent = "40";
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().NotBeNull(editor.StatusText);
        var args = WorkflowDefinitionSerializer.Deserialize(editor.Result!.WorkflowText)[0].Args;
        args.GetProperty("options").EnumerateObject().Select(p => p.Name).Should().Equal("clarify", "process");
        args.GetProperty("options").GetProperty("clarify").GetString().Should().Be("Précisions essentielles 漢😀");
        args.GetProperty("acceptance").GetProperty("minTopProbability").GetDouble().Should().Be(.925);
        args.GetProperty("acceptance").GetProperty("minMargin").GetDouble().Should().Be(.4);
        service.VerifyNoOtherCalls();
    }

    /// <summary>Invalid identifiers, duplicates and literal placeholders are blocked before saving or testing.</summary>
    [Theory]
    [InlineData("process")]
    [InlineData("PROCESS")]
    [InlineData("1invalid")]
    [InlineData("{{step.output}}")]
    public async Task InvalidOptionCannotApplyOrTest(string key)
    {
        var (editor, service) = Editor();
        editor.EvaluationOptions[1].Key = key;
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().BeNull();
        editor.StatusText.Should().NotBeEmpty();
        editor.EvaluationSample = "A sample request.";
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationMessage.Should().NotBeEmpty();
        service.VerifyNoOtherCalls();
    }

    /// <summary>Overrides may tighten but never weaken the globally configured policy.</summary>
    [Theory]
    [InlineData("79.9")]
    [InlineData("101")]
    [InlineData("NaN")]
    [InlineData("85,5")]
    public async Task InvalidThresholdCannotApplyOrTest(string percent)
    {
        var (editor, service) = Editor();
        editor.OverrideEvaluationTop = true;
        editor.EvaluationTopPercent = percent;
        editor.EvaluationSample = "Sample";
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().BeNull();
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationMessage.Should().NotBeEmpty();
        service.VerifyNoOtherCalls();
    }

    /// <summary>Testing explicitly supplied content calls only the common service and preserves workflow arguments.</summary>
    [Theory]
    [InlineData("accepted")]
    [InlineData("uncertain")]
    [InlineData("unavailable")]
    public async Task ExplicitTestDisplaysAllScoresWithoutChangingDraft(string status)
    {
        var (editor, service) = Editor();
        var before = editor.Arguments.Select(a => (a.IsIncluded, a.Value)).ToArray();
        editor.EvaluationSample = "Explicit test source";
        service.Setup(s => s.EvaluateAsync("Explicit test source", "Is this request complete?", It.IsAny<JsonElement>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(Result(status));
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationSummary.Should().Contain(status);
        editor.EvaluationSummary.Should().Contain(status == "unavailable" ? "Margin: unavailable" : "Margin: 80 pp");
        editor.EvaluationScores.Select(s => s.Identifier).Should().Equal("process", "clarify");
        editor.EvaluationScores[0].Probability.Should().Be(status == "unavailable" ? "unavailable" : "90%");
        editor.EvaluationTiming.Should().Contain("Gate: 3 ms").And.Contain("Total (including cleanup): 30 ms");
        editor.Arguments.Select(a => (a.IsIncluded, a.Value)).Should().Equal(before);
        editor.HasChanges.Should().BeFalse();
        service.Verify(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), null,
            It.IsAny<CancellationToken>()), Times.Once);
        service.VerifyNoOtherCalls();
    }

    /// <summary>A source placeholder is not resolved by the inspector and always requires explicit sample text.</summary>
    [Fact]
    public async Task MissingSampleDoesNotResolvePlaceholder()
    {
        var (editor, service) = Editor();
        editor.EvaluationSourceArgument!.Value = "{{fetch.output.body}}";
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationMessage.Should().Contain("explicit sample");
        service.VerifyNoOtherCalls();
    }

    /// <summary>Global enablement and an unchecked permission proposal prevent inference.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TestRespectsEnablementAndTaskPermission(bool permitted, bool enabled)
    {
        var (editor, service) = Editor(permitted, enabled);
        editor.EvaluationSample = "Sample";
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationMessage.Should().Contain(enabled ? "Allow the decision" : "Enable decision");
        service.VerifyNoOtherCalls();
    }

    /// <summary>A manually accepted draft permission can authorize a sample without persisting the task.</summary>
    [Fact]
    public async Task ExplicitPermissionProposalAuthorizesOnlyDraftTest()
    {
        var (editor, service) = Editor(permitted: false);
        editor.AuthorizationProposals.Single(p => p.Kind == "Tool group" && p.Key == "decision").IsAccepted = true;
        editor.EvaluationSample = "Sample";
        service.Setup(s => s.EvaluateAsync("Sample", It.IsAny<string>(), It.IsAny<JsonElement>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(Result());
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationSummary.Should().Contain("accepted");
        editor.Result.Should().BeNull();
    }

    /// <summary>The explicit inspector uses pending field edits without committing them to raw arguments.</summary>
    [Fact]
    public async Task TestUsesPendingOptionsAndAcceptanceWithoutCommitting()
    {
        var (editor, service) = Editor();
        var originalOptions = editor.Arguments.Single(a => a.Definition.Name == "options").Value;
        editor.EvaluationOptions[0].Description = "Edited option";
        editor.OverrideEvaluationTop = true;
        editor.EvaluationTopPercent = "90";
        editor.EvaluationSample = "Sample";
        service.Setup(s => s.EvaluateAsync("Sample", It.IsAny<string>(),
            It.Is<JsonElement>(j => j.GetProperty("process").GetString() == "Edited option"),
            It.Is<JsonElement?>(j => j.HasValue && j.Value.GetProperty("minTopProbability").GetDouble() == .9),
            It.IsAny<CancellationToken>())).ReturnsAsync(Result());
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationSummary.Should().Contain("accepted");
        editor.Arguments.Single(a => a.Definition.Name == "options").Value.Should().Be(originalOptions);
        editor.Arguments.Single(a => a.Definition.Name == "acceptance").IsIncluded.Should().BeFalse();
    }

    /// <summary>Cancelled evaluation propagates cancellation and produces no saved or scored result.</summary>
    [Fact]
    public async Task TestCanBeCancelled()
    {
        var (editor, service) = Editor();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), null,
            It.IsAny<CancellationToken>())).Returns(async (string _, string _, JsonElement _, JsonElement? _, CancellationToken token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return Result();
            });
        editor.EvaluationSample = "Sample";
        var operation = editor.TestDecisionCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        editor.TestDecisionCancelCommand.Execute(null);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        editor.EvaluationMessage.Should().Contain("cancelled");
        editor.EvaluationScores.Should().BeEmpty();
        editor.IsTestingDecision.Should().BeFalse();
        editor.HasChanges.Should().BeFalse();
    }

    /// <summary>Closing the dialog prevents even a non-cooperative service from publishing a late result.</summary>
    [Fact]
    public async Task ClosedEditorSuppressesLateResult()
    {
        var (editor, service) = Editor();
        var pending = new TaskCompletionSource<DecisionEvaluationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), null,
            It.IsAny<CancellationToken>())).Returns(pending.Task);
        editor.EvaluationSample = "Sample";
        var operation = editor.TestDecisionCommand.ExecuteAsync(null);
        editor.CancelDecisionTest();
        pending.SetResult(Result());
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        editor.EvaluationSummary.Should().BeEmpty();
        editor.EvaluationScores.Should().BeEmpty();
    }

    /// <summary>Raw JSON edits reload ordered fields and malformed JSON cannot be silently overwritten.</summary>
    [Fact]
    public void RawJsonIsAuthoritativeAndInvalidJsonIsPreserved()
    {
        var (editor, _) = Editor();
        var raw = editor.Arguments.Single(a => a.Definition.Name == "options");
        raw.Value = "{\"review\":\"Manual review\",\"process\":\"Proceed\"}";
        editor.EvaluationOptions.Select(o => o.Key).Should().Equal("review", "process");
        raw.Value = "{broken";
        editor.EvaluationFieldsError.Should().NotBeEmpty();
        editor.AddEvaluationOptionCommand.Execute(null);
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().BeNull();
        raw.Value.Should().Be("{broken");
    }

    /// <summary>Option controls preserve the contract's minimum and maximum count.</summary>
    [Fact]
    public void OptionControlsAreBounded()
    {
        var (editor, _) = Editor();
        editor.SelectedEvaluationOption = editor.EvaluationOptions[0];
        editor.RemoveEvaluationOptionCommand.Execute(null);
        editor.EvaluationOptions.Should().HaveCount(2);
        for (var index = 0; index < 30; index++) editor.AddEvaluationOptionCommand.Execute(null);
        editor.EvaluationOptions.Should().HaveCount(24);
    }

    /// <summary>A sample supplies test content even while the persisted source field is still being authored.</summary>
    [Fact]
    public async Task SampleDoesNotRequirePersistedSource()
    {
        var (editor, service) = Editor();
        editor.EvaluationSourceArgument!.Value = string.Empty;
        editor.EvaluationSample = "Sample";
        service.Setup(s => s.EvaluateAsync("Sample", It.IsAny<string>(), It.IsAny<JsonElement>(), null,
            It.IsAny<CancellationToken>())).ReturnsAsync(Result());
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationSummary.Should().Contain("accepted");
        editor.EvaluationSourceArgument.Value.Should().BeEmpty();
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().BeNull();
        editor.StatusText.Should().NotBeEmpty();
    }

    /// <summary>Existing acceptance presence and precise values survive an unchanged edit.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"minTopProbability\":0.9123456789012345,\"minMargin\":0.3}")]
    public void ExistingAcceptanceIsPreservedOnNoOp(string acceptance)
    {
        using var source = JsonDocument.Parse(Workflow);
        var args = source.RootElement[0].GetProperty("args").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        args["acceptance"] = JsonDocument.Parse(acceptance).RootElement.Clone();
        var workflow = JsonSerializer.Serialize(new[] { new { step = "decision.evaluate", id = "readiness", args } });
        var (editor, service) = Editor(workflow: workflow);
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().BeNull();
        editor.IsApplyCloseRequested.Should().BeTrue(editor.StatusText);
        service.VerifyNoOtherCalls();
    }
}
