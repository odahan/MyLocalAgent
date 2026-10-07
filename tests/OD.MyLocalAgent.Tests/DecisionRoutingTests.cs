using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using OD.MyLocalAgent.Workflows;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies integrated decision routing without live model calls or external side effects.</summary>
public sealed class DecisionRoutingTests
{
    private const string Workflow = """
        [
          {"step":"decision.evaluate","id":"route","args":{"source":"Explicit source","question":"Proceed?","options":{"process":"Ready","review":"Human review"}},
           "decisionRoutes":{"choices":{"process":"process","review":"review"},"uncertain":"uncertain","unavailable":"unavailable"}},
          {"step":"ai.summarize","id":"process","next":"@end","args":{"text":"{{route.output}}"}},
          {"step":"ai.summarize","id":"review","next":"@end","args":{"text":"{{route.output}}"}},
          {"step":"ai.summarize","id":"uncertain","next":"@end","args":{"text":"{{route.output}}"}},
          {"step":"ai.summarize","id":"unavailable","next":"@end","args":{"text":"{{route.output}}"}}
        ]
        """;

    private static ScheduledTask TaskDefinition(string workflow = Workflow) => new()
    {
        Id = "decision-routing-test", Name = "Decision routing", Schedule = "0 9 * * *",
        Enabled = false, Tools = ["decision"], Workflow = WorkflowDefinitionSerializer.Deserialize(workflow),
    };

    /// <summary>Both engines route accepted semantic review separately from uncertainty and failures.</summary>
    [Theory]
    [InlineData("NativeDecision", "accepted", "process", "process")]
    [InlineData("NativeDecision", "accepted", "review", "review")]
    [InlineData("NativeDecision", "uncertain", "process", "uncertain")]
    [InlineData("NativeDecision", "unavailable", null, "unavailable")]
    [InlineData("CentralLlm", "accepted", "process", "process")]
    [InlineData("CentralLlm", "accepted", "review", "review")]
    [InlineData("CentralLlm", "unavailable", null, "unavailable")]
    public async Task ActualRegistryAndEngineExecuteOnlySelectedDestination(string engineName, string status, string? choice, string destination)
    {
        var task = TaskDefinition();
        ScheduledTaskValidator.Validate(task);
        var decision = new Mock<IDecisionEvaluationService>();
        var response = new DecisionEvaluationResult(2, status, choice, null, null, null, null, null, [], "provider", "model", new(0, 1, 1, false))
        {
            Engine = engineName, AcceptanceBasis = engineName == "CentralLlm" ? "validatedChoice" : "nativeThresholds",
            Capabilities = new(engineName == "NativeDecision"),
        };
        decision.Setup(s => s.EvaluateAsync("Explicit source", "Proceed?", It.IsAny<JsonElement>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        using var provider = new ServiceCollection().AddSingleton(decision.Object).BuildServiceProvider();
        var registry = new ToolRegistry(ToolCatalog.Default, new ToolTargetActivator(provider, ToolCatalog.Default));
        var workflowEngine = new SequentialWorkflowEngine(registry, [new EchoSkill()], Mock.Of<IMyLocalAgentService>(),
            TimeProvider.System, NullLogger<SequentialWorkflowEngine>.Instance, decisionEvaluation: decision.Object);
        var result = await workflowEngine.ExecuteAsync(task, default);
        result.Success.Should().BeTrue();
        result.Steps.Select(s => s.Id).Should().Equal("route", destination);
        result.FinalText.Should().Contain(status).And.Contain(response.AcceptanceBasis);
        decision.Verify(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Closed route contracts reject missing, dynamic, duplicate or incompatible authoring.</summary>
    [Theory]
    [InlineData("\"review\":\"review\"", "\"other\":\"review\"")]
    [InlineData("\"uncertain\":\"uncertain\",", "")]
    [InlineData("\"unavailable\":\"unavailable\"", "\"unavailable\":\"\"")]
    [InlineData("\"process\":\"process\"", "\"process\":\"{{task.id}}\"")]
    [InlineData("\"process\":\"process\"", "\"process\":\"missing\"")]
    [InlineData("\"process\":\"process\"", "\"process\":\"route\"")]
    [InlineData("\"uncertain\":\"uncertain\"", "\"uncertain\":\"uncertain\",\"engine\":\"CentralLlm\"")]
    [InlineData("\"review\":\"review\"", "\"review\":\"review\",\"review\":\"review\"")]
    [InlineData("\"decisionRoutes\":", "\"next\":\"process\",\"decisionRoutes\":")]
    public void InvalidRouteContractsCannotSave(string from, string to)
    {
        FluentActions.Invoking(() => ScheduledTaskValidator.Validate(TaskDefinition(Workflow.Replace(from, to, StringComparison.Ordinal))))
            .Should().Throw<WorkflowValidationException>();
    }

    /// <summary>Every route enters guaranteed-output analysis, including uncertainty branches.</summary>
    [Fact]
    public void BranchOutputCannotBeConsumedFromOtherRoutes()
    {
        var workflow = TaskDefinition().Workflow.ToArray();
        workflow[2] = workflow[2] with { Args = JsonSerializer.SerializeToElement(new { text = "{{process.output}}" }) };
        FluentActions.Invoking(() => ScheduledTaskValidator.Validate(TaskDefinition() with { Workflow = workflow }))
            .Should().Throw<WorkflowValidationException>().WithMessage("*not guaranteed*");
    }

    /// <summary>Permissions remain mandatory and route metadata cannot attach to another tool.</summary>
    [Fact]
    public void AuthorizationAndAliasRemainRequired()
    {
        FluentActions.Invoking(() => ScheduledTaskValidator.Validate(TaskDefinition() with { Tools = [] }))
            .Should().Throw<WorkflowValidationException>();
        var workflow = TaskDefinition().Workflow.ToArray();
        workflow[1] = workflow[1] with { DecisionRoutes = workflow[0].DecisionRoutes };
        FluentActions.Invoking(() => ScheduledTaskValidator.Validate(TaskDefinition() with { Workflow = workflow }))
            .Should().Throw<WorkflowValidationException>();
    }

    /// <summary>Invalid result versions and unmapped choices fail instead of following a normal destination.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"status\":\"accepted\",\"choice\":\"process\"}")]
    [InlineData("{\"schemaVersion\":2,\"status\":\"accepted\",\"choice\":\"other\"}")]
    [InlineData("{\"schemaVersion\":2,\"status\":\"invalid\",\"choice\":\"process\"}")]
    public void InvalidResultCannotSelectDestination(string output)
    {
        FluentActions.Invoking(() => DecisionRouteDefinition.Parse(TaskDefinition().Workflow[0]).SelectTarget(output))
            .Should().Throw<WorkflowValidationException>();
    }

    /// <summary>Route metadata round trips and deletion analysis includes all destinations.</summary>
    [Fact]
    public void SerializationAndDependenciesRetainRoutes()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(WorkflowDefinitionSerializer.Serialize(TaskDefinition().Workflow));
        DecisionRouteDefinition.Parse(workflow[0]).Targets.Should().Equal("process", "review", "uncertain", "unavailable");
        foreach (var index in Enumerable.Range(1, 4))
            WorkflowStepDependencyAnalyzer.FindLaterReferences(workflow, index).Should().Contain(d => d.StepIndex == 0);
        WorkflowDescriptionFormatter.Format(workflow).Should().Contain("decisionRoutes");
    }

    /// <summary>Editing option descriptions preserves routes and never invokes inference on Apply.</summary>
    [Fact]
    public void EditorAppliesRoutesAndNoOpEditPreservesContract()
    {
        var service = new Mock<IDecisionEvaluationService>(MockBehavior.Strict);
        var editor = CreateEditor(service);
        editor.RouteByDecision.Should().BeTrue();
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().BeNull();
        editor.StatusText.Should().BeEmpty();
        editor = CreateEditor(service);
        editor.EvaluationOptions[0].Description = "Ready to process";
        editor.EvaluationOptions[0].Target = "review";
        editor.EvaluationOptions[1].Target = "process";
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().NotBeNull(editor.StatusText);
        var step = WorkflowDefinitionSerializer.Deserialize(editor.Result!.WorkflowText)[0];
        DecisionRouteDefinition.Parse(step).Choices["process"].Should().Be("review");
        service.VerifyNoOtherCalls();
    }

    /// <summary>Missing destinations block Apply while an explicit sample test never runs any branch.</summary>
    [Fact]
    public async Task EditorTestDoesNotRunDestinations()
    {
        var service = new Mock<IDecisionEvaluationService>();
        service.Setup(s => s.EvaluateAsync("sample", It.IsAny<string>(), It.IsAny<JsonElement>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DecisionEvaluationResult(2, "unavailable", null, null, null, null, null, null, ["ModelMissing"], "p", "m", new(0, 0, 0, null)));
        var editor = CreateEditor(service);
        editor.EvaluationUncertainTarget = "";
        editor.ApplyCommand.Execute(null);
        editor.Result.Should().BeNull();
        editor.StatusText.Should().NotBeEmpty();
        editor.EvaluationSample = "sample";
        await editor.TestDecisionCommand.ExecuteAsync(null);
        editor.EvaluationSummary.Should().Contain("unavailable");
        editor.Result.Should().BeNull();
        service.Verify(s => s.EvaluateAsync("sample", It.IsAny<string>(), It.IsAny<JsonElement>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static WorkflowStepEditorViewModel CreateEditor(Mock<IDecisionEvaluationService> service)
    {
        var editor = new WorkflowStepEditorViewModel(decisionEvaluation: service.Object,
            decisionOptions: new MutableOptionsMonitor<DecisionOptions>(new() { Enabled = true }));
        editor.Initialize(new(Workflow, Workflow.IndexOf("decision.evaluate", StringComparison.Ordinal), WorkflowStepEditorMode.Edit,
            ["decision"], [], [], [], WorkflowErrorPolicy.StopOnError, 0.2, 1) { DraftTask = TaskDefinition() });
        return editor;
    }

    /// <summary>Execution exceptions preserve Stop/Retry/Continue semantics instead of becoming decision unavailability.</summary>
    [Theory]
    [InlineData(WorkflowErrorPolicy.StopOnError, 1, false)]
    [InlineData(WorkflowErrorPolicy.Retry, 2, true)]
    [InlineData(WorkflowErrorPolicy.ContinueOnError, 1, false)]
    public async Task ExecutionErrorsRetainPolicy(WorkflowErrorPolicy policy, int calls, bool success)
    {
        var task = TaskDefinition() with { ErrorPolicy = policy };
        var steps = task.Workflow.ToArray();
        steps[0] = steps[0] with { RetryCount = 1, RetryBackoffSeconds = 0 };
        steps[1] = steps[1] with { Args = JsonSerializer.SerializeToElement(new { text = "Continuation" }) };
        task = task with { Workflow = steps };
        ScheduledTaskValidator.Validate(task);
        var invocationCount = 0;
        var service = new Mock<IDecisionEvaluationService>();
        service.Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), null, It.IsAny<CancellationToken>()))
            .Returns(() => ++invocationCount == 1
                ? Task.FromException<DecisionEvaluationResult>(new InvalidOperationException("Execution failed"))
                : Task.FromResult(new DecisionEvaluationResult(2, "accepted", "review", null, null, null, null, null, [], "p", "m", new(0, 0, 0, null))));
        using var provider = new ServiceCollection().AddSingleton(service.Object).BuildServiceProvider();
        var registry = new ToolRegistry(ToolCatalog.Default, new ToolTargetActivator(provider, ToolCatalog.Default));
        var engine = new SequentialWorkflowEngine(registry, [new EchoSkill()], Mock.Of<IMyLocalAgentService>(), TimeProvider.System,
            NullLogger<SequentialWorkflowEngine>.Instance);
        var result = await engine.ExecuteAsync(task, default);
        result.Success.Should().Be(success);
        invocationCount.Should().Be(calls);
        result.Steps.Select(s => s.Id).Should().Equal(policy switch
        {
            WorkflowErrorPolicy.Retry => ["route", "review"],
            WorkflowErrorPolicy.ContinueOnError => ["route", "process"],
            _ => new[] { "route" },
        });
    }

    /// <summary>Caller cancellation propagates before any destination executes.</summary>
    [Fact]
    public async Task CancellationDoesNotRoute()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<IDecisionEvaluationService>();
        service.Setup(s => s.EvaluateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>(), null, It.IsAny<CancellationToken>()))
            .Returns((string _, string _, JsonElement _, JsonElement? _, CancellationToken token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<DecisionEvaluationResult>(token);
            });
        using var provider = new ServiceCollection().AddSingleton(service.Object).BuildServiceProvider();
        var registry = new ToolRegistry(ToolCatalog.Default, new ToolTargetActivator(provider, ToolCatalog.Default));
        var engine = new SequentialWorkflowEngine(registry, [new EchoSkill()], Mock.Of<IMyLocalAgentService>(), TimeProvider.System,
            NullLogger<SequentialWorkflowEngine>.Instance);
        await FluentActions.Awaiting(() => engine.ExecuteAsync(TaskDefinition(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>The shipped disabled example is validated through the real task serializer.</summary>
    [Fact]
    public void ShippedIntegratedExampleIsValid()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OD.MyLocalAgent.sln"))) root = root.Parent;
        var task = ScheduledTaskSerializer.Deserialize(File.ReadAllText(Path.Combine(root!.FullName, "Docs", "samples", "decision-route-readiness.json")));
        task.Enabled.Should().BeFalse();
        DecisionRouteDefinition.Parse(task.Workflow[0]).Choices.Keys.Should().Contain("review");
    }

    private sealed class EchoSkill : ISkill
    {
        public string Name => "summarize";
        public string AgentFunctionName => "echo";
        public string Description => "Returns explicitly supplied text without inference.";
        public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken) => Task.FromResult(input);
    }
}
