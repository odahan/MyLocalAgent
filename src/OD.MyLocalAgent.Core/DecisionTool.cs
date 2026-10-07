using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Exposes explicit, read-only evaluation only to authorized workflows.</summary>
[ToolGroup("decision", "Decision evaluation", "Workflow-only decisions using the global engine; inspect acceptance basis and route on status before choice.", 120,
    Activation = ToolActivationKind.Contextual, RegisterTasklessProjection = false)]
public sealed class DecisionTool(ToolExecutionContext context, IDecisionEvaluationService service)
{
    /// <summary>Evaluates one choice question and returns normalized scores and an explicit status.</summary>
    [Tool("decision.evaluate", "Evaluate a decision using explicit text and 2–24 options. Route on status first; only accepted choices may drive normal routing. Optional top-level decisionRoutes maps every option plus uncertain/unavailable to destinations (Route by decision). Global engine only; no overrides or fallback. Version 2 central results have null scores.", 100, WorkflowOnly = true)]
    public async Task<string> EvaluateAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Explicit non-empty content to evaluate, or a previous output placeholder.", IsLongText = true, Example = "{{request.output}}")] string source,
        [ToolArgument(WorkflowArgumentKind.String, "One literal decision question, at most 1000 characters.", IsLongText = true, Example = "Is the request complete?")] string question,
        [ToolArgument(WorkflowArgumentKind.Object, "2–24 stable identifiers mapped to descriptions of at most 400 characters.", IsLongText = true, Example = "{\"process\":\"Essential information is present.\",\"clarify\":\"Essential information is missing.\"}")] JsonElement options,
        [ToolArgument(WorkflowArgumentKind.Object, "Native-only minTopProbability and minMargin in [0,1]; tighten Settings defaults. Explicit acceptance is incompatible with the central LLM.", Example = "{\"minTopProbability\":0.9,\"minMargin\":0.3}")] JsonElement? acceptance = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.TaskId) ||
            context.Surface is not (ToolInvocationSurface.Workflow or ToolInvocationSurface.WorkflowTest))
            throw new WorkflowValidationException("Decision evaluation is available only inside an authorized workflow.");
        var result = context.DecisionSnapshot is { } snapshot
            ? await service.EvaluateAsync(snapshot, source, question, options, acceptance, cancellationToken).ConfigureAwait(false)
            : await service.EvaluateAsync(source, question, options, acceptance, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
