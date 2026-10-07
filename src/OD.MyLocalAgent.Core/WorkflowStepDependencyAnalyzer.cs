using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Finds placeholder and routing references to one workflow step without changing workflow data.</summary>
public static class WorkflowStepDependencyAnalyzer
{
    /// <summary>Returns steps and fields that reference the selected effective id.</summary>
    public static IReadOnlyList<WorkflowStepDependency> FindLaterReferences(IReadOnlyList<WorkflowStepDefinition> workflow, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (targetIndex < 0 || targetIndex >= workflow.Count) throw new ArgumentOutOfRangeException(nameof(targetIndex));
        var id = workflow[targetIndex].EffectiveId;
        var matches = new Dictionary<int, WorkflowStepDependency>();
        IReadOnlySet<string> available = new HashSet<string>([id], StringComparer.OrdinalIgnoreCase);
        for (var index = targetIndex + 1; index < workflow.Count; index++)
        {
            var arguments = ReferencingArguments(workflow[index].Args, id, available).ToList();
            if (ReferencesOutput(workflow[index].ForEach, id, available))
            {
                arguments.Add("forEach");
            }

            if (arguments.Count > 0)
            {
                matches[index] = new WorkflowStepDependency(index, workflow[index].EffectiveId, workflow[index].Step, arguments);
            }
        }

        for (var index = 0; index < workflow.Count; index++)
        {
            if (index == targetIndex)
            {
                continue;
            }

            var routing = ReferencingRoutes(workflow[index], id);
            if (routing.Count == 0)
            {
                continue;
            }

            if (matches.TryGetValue(index, out var existing))
            {
                matches[index] = existing with { ArgumentNames = [.. existing.ArgumentNames, .. routing] };
            }
            else
            {
                matches[index] = new WorkflowStepDependency(index, workflow[index].EffectiveId, workflow[index].Step, routing);
            }
        }

        return matches.Values.OrderBy(static match => match.StepIndex).ToArray();
    }

    private static IReadOnlyList<string> ReferencingRoutes(WorkflowStepDefinition step, string id)
    {
        var routes = new List<string>();
        if (step.DecisionRoutes is not null)
        {
            var decision = DecisionRouteDefinition.Parse(step);
            foreach (var choice in decision.Choices)
                if (string.Equals(choice.Value, id, StringComparison.OrdinalIgnoreCase)) routes.Add("decisionRoutes.choices." + choice.Key);
            if (string.Equals(decision.Uncertain, id, StringComparison.OrdinalIgnoreCase)) routes.Add("decisionRoutes.uncertain");
            if (string.Equals(decision.Unavailable, id, StringComparison.OrdinalIgnoreCase)) routes.Add("decisionRoutes.unavailable");
        }
        if (string.Equals(step.Next, id, StringComparison.OrdinalIgnoreCase))
        {
            routes.Add("next");
        }

        if (string.Equals(step.Step, "flow.decision", StringComparison.OrdinalIgnoreCase) && step.Args.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "target", "fallback" })
            {
                if (step.Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                    string.Equals(value.GetString(), id, StringComparison.OrdinalIgnoreCase))
                {
                    routes.Add(name);
                }
            }
        }

        return routes;
    }

    private static IReadOnlyList<string> ReferencingArguments(JsonElement arguments, string id, IReadOnlySet<string> available)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return [];
        return arguments.EnumerateObject()
            .Where(property => EnumerateStrings(property.Value).SelectMany(WorkflowPlaceholderCatalog.Extract).Any(marker => marker.StartsWith(id + ".output", StringComparison.OrdinalIgnoreCase) && WorkflowPlaceholderCatalog.IsValid(marker, available, out _)))
            .Select(static property => property.Name)
            .ToArray();
    }

    private static bool ReferencesOutput(string? text, string id, IReadOnlySet<string> available) =>
        text is not null && WorkflowPlaceholderCatalog.Extract(text).Any(
            marker => marker.StartsWith(id + ".output", StringComparison.OrdinalIgnoreCase) &&
                      WorkflowPlaceholderCatalog.IsValid(marker, available, out _));

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) { yield return element.GetString() ?? string.Empty; yield break; }
        if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) foreach (var text in EnumerateStrings(item)) yield return text;
        if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) foreach (var text in EnumerateStrings(property.Value)) yield return text;
    }
}

/// <summary>Describes one step that depends on a selected step id or output.</summary>
public sealed record WorkflowStepDependency(int StepIndex, string EffectiveId, string Alias, IReadOnlyList<string> ArgumentNames);
