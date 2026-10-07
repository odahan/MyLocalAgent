using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Prevents a global engine change from silently bypassing authored native numeric gates.</summary>
public static class DecisionWorkflowCompatibility
{
    /// <summary>Checks explicit acceptance and score consumers before the first workflow step executes.</summary>
    public static void Validate(IReadOnlyList<WorkflowStepDefinition> workflow, DecisionExecutionSnapshot? snapshot)
    {
        if (snapshot?.Engine != "CentralLlm") return;
        var ids = workflow.Select((step, index) => (step.EffectiveId, index))
            .ToDictionary(p => p.EffectiveId, p => p.index, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < workflow.Count; index++)
        {
            var step = workflow[index];
            if (step.Step is "decision.evaluate" or "gmailDecision.readClassify")
            {
                if (step.Args.TryGetProperty("acceptance", out _))
                    throw new WorkflowValidationException($"Step '{step.EffectiveId}' has explicit native acceptance incompatible with CentralLlm. Edit the workflow deliberately or select NativeDecision.");
                if (step.Step == "gmailDecision.readClassify")
                    GmailDecisionMigration.Validate(step.Args.TryGetProperty("classificationMode", out var mode) ? mode.GetString()! : "Global",
                        step.Args.TryGetProperty("arbitration", out var arbitration) ? arbitration : null,
                        step.Args.TryGetProperty("unavailableBehavior", out var unavailable) ? unavailable.GetString() : null, snapshot.Engine, null);
            }
            if (step.Step != "flow.decision") continue;
            var gate = WorkflowDecisionDefinition.Parse(step.Args);
            if (gate.Operator is not (WorkflowDecisionOperator.GreaterThan or WorkflowDecisionOperator.GreaterThanOrEqual or
                WorkflowDecisionOperator.LessThan or WorkflowDecisionOperator.LessThanOrEqual) &&
                !(gate.Operator == WorkflowDecisionOperator.Equals && gate.Value?.ValueKind == JsonValueKind.Number)) continue;
            var marker = WorkflowPlaceholderCatalog.Extract(gate.Source).SingleOrDefault();
            if (marker is null || !marker.Contains(".output", StringComparison.Ordinal)) continue;
            var sourceId = marker[..marker.IndexOf(".output", StringComparison.Ordinal)];
            if (!ids.TryGetValue(sourceId, out var producer) ||
                workflow[producer].Step is not ("decision.evaluate" or "gmailDecision.readClassify")) continue;
            var selector = marker[(sourceId.Length + ".output".Length)..].TrimStart('.');
            var path = string.IsNullOrEmpty(selector) ? gate.Path : selector + (gate.Path is null ? "" : "." + gate.Path);
            if (path is null || !(path is "topProbability" or "margin" or "confidence" ||
                path.StartsWith("probabilities.", StringComparison.Ordinal) ||
                path.Contains(".decisionScores.", StringComparison.Ordinal))) continue;
            var guarded = Enumerable.Range(0, index).Any(guardIndex =>
            {
                var guardStep = workflow[guardIndex];
                if (guardStep.Step != "flow.decision") return false;
                var guard = WorkflowDecisionDefinition.Parse(guardStep.Args);
                if (guard.Source != "{{" + sourceId + ".output}}" || guard.Path != "capabilities.scores" ||
                    guard.Operator != WorkflowDecisionOperator.Equals || guard.Negate || guard.Value?.ValueKind != JsonValueKind.True)
                    return false;
                return !CanReach(index, guardIndex, null) && !CanReach(index, -1, (guardIndex, guard.Target));
            });
            if (!guarded)
                throw new WorkflowValidationException($"Score route '{step.EffectiveId}' requires a dominating capabilities.scores = true guard before CentralLlm can run. Native scores cannot be silently ignored.");
        }

        bool CanReach(int target, int removedNode, (int Index, string Target)? removedEdge)
        {
            var pending = new Stack<int>();
            var seen = new HashSet<int>();
            pending.Push(0);
            while (pending.TryPop(out var current))
            {
                if (current == removedNode || !seen.Add(current)) continue;
                if (current == target) return true;
                var step = workflow[current];
                var edges = step.DecisionRoutes is not null
                    ? DecisionRouteDefinition.Parse(step).Targets.Cast<string?>().ToList()
                    : new List<string?> { step.Next ?? (current + 1 < workflow.Count ? workflow[current + 1].EffectiveId : null) };
                if (step.DecisionRoutes is not null && step.OnError != WorkflowErrorPolicy.StopOnError && step.OnError != WorkflowErrorPolicy.Retry)
                    edges.Add(current + 1 < workflow.Count ? workflow[current + 1].EffectiveId : null);
                if (step.Step == "flow.decision")
                {
                    var route = WorkflowDecisionDefinition.Parse(step.Args);
                    edges.Add(route.Target);
                    edges.Add(route.Fallback);
                }
                for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                {
                    var edge = edges[edgeIndex];
                    if (edge is not null && ids.TryGetValue(edge, out var next) &&
                        !(removedEdge is { } removed && removed.Index == current && removed.Target == edge && edgeIndex == 1)) pending.Push(next);
                }
            }
            return false;
        }
    }
}
