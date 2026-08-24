using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Finds later placeholder references to one workflow step without changing workflow data.</summary>
public static class WorkflowStepDependencyAnalyzer
{
    /// <summary>Returns later steps and argument names that reference the selected effective id.</summary>
    public static IReadOnlyList<WorkflowStepDependency> FindLaterReferences(IReadOnlyList<WorkflowStepDefinition> workflow, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (targetIndex < 0 || targetIndex >= workflow.Count) throw new ArgumentOutOfRangeException(nameof(targetIndex));
        var id = workflow[targetIndex].EffectiveId;
        var matches = new List<WorkflowStepDependency>();
        IReadOnlySet<string> available = new HashSet<string>([id], StringComparer.OrdinalIgnoreCase);
        for (var index = targetIndex + 1; index < workflow.Count; index++)
        {
            var arguments = ReferencingArguments(workflow[index].Args, id, available);
            if (arguments.Count > 0)
            {
                matches.Add(new WorkflowStepDependency(index, workflow[index].EffectiveId, workflow[index].Step, arguments));
            }
        }
        return matches;
    }

    private static IReadOnlyList<string> ReferencingArguments(JsonElement arguments, string id, IReadOnlySet<string> available)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return [];
        return arguments.EnumerateObject()
            .Where(property => EnumerateStrings(property.Value).SelectMany(WorkflowPlaceholderCatalog.Extract).Any(marker => marker.StartsWith(id + ".output", StringComparison.OrdinalIgnoreCase) && WorkflowPlaceholderCatalog.IsValid(marker, available, out _)))
            .Select(static property => property.Name)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) { yield return element.GetString() ?? string.Empty; yield break; }
        if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) foreach (var text in EnumerateStrings(item)) yield return text;
        if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) foreach (var text in EnumerateStrings(property.Value)) yield return text;
    }
}

/// <summary>Describes one later step that depends on a selected step output.</summary>
public sealed record WorkflowStepDependency(int StepIndex, string EffectiveId, string Alias, IReadOnlyList<string> ArgumentNames);
