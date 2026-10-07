using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Defines exhaustive literal destinations for the common decision evaluator.</summary>
public sealed record DecisionRouteDefinition(
    IReadOnlyDictionary<string, string> Choices,
    string Uncertain,
    string Unavailable)
{
    /// <summary>Gets all destinations contributing to workflow graph validation.</summary>
    public IEnumerable<string> Targets => Choices.Values.Append(Uncertain).Append(Unavailable);

    /// <summary>Validates the persisted route contract against the authored option identifiers.</summary>
    public static DecisionRouteDefinition Parse(WorkflowStepDefinition step)
    {
        if (step.Step != "decision.evaluate" || step.DecisionRoutes is not { } json ||
            step.Next is not null || step.ForEach is not null)
            throw new WorkflowValidationException("decisionRoutes requires decision.evaluate without next or forEach.");

        DecisionEvaluateContract.Validate(step.Args);
        if (json.ValueKind != JsonValueKind.Object)
            throw new WorkflowValidationException("decisionRoutes must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in json.EnumerateObject())
            if (property.Name is not ("choices" or "uncertain" or "unavailable") || !names.Add(property.Name))
                throw new WorkflowValidationException("decisionRoutes contains an unknown or duplicate property.");
        if (!json.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Object)
            throw new WorkflowValidationException("decisionRoutes requires a choices object.");
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in choices.EnumerateObject())
            if (!targets.TryAdd(property.Name, ReadTarget(property.Value)))
                throw new WorkflowValidationException("Decision route option identifiers must be unique.");
        var options = step.Args.GetProperty("options").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        if (!options.SetEquals(targets.Keys))
            throw new WorkflowValidationException("Decision routes must map every option exactly once, with no extra option.");
        return new(targets, RequiredTarget(json, "uncertain"), RequiredTarget(json, "unavailable"));
    }

    /// <summary>Selects a destination only from a supported version 2 result and its status.</summary>
    public string SelectTarget(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 2)
                throw new WorkflowValidationException("Decision routing requires a version 2 evaluation result.");
            var status = root.GetProperty("status").GetString();
            return status switch
            {
                "uncertain" => Uncertain,
                "unavailable" => Unavailable,
                "accepted" when root.GetProperty("choice").ValueKind == JsonValueKind.String &&
                    Choices.TryGetValue(root.GetProperty("choice").GetString()!, out var target) => target,
                _ => throw new WorkflowValidationException("Decision routing received an invalid status or an unmapped accepted choice."),
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new WorkflowValidationException("Decision routing received an invalid evaluation result.", exception);
        }
    }

    private static string RequiredTarget(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) ? ReadTarget(value) :
            throw new WorkflowValidationException($"decisionRoutes requires an explicit {name} destination.");

    private static string ReadTarget(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) ||
            value.GetString()!.Contains("{{", StringComparison.Ordinal))
            throw new WorkflowValidationException("Decision destinations must be non-empty literal step ids or @end.");
        return value.GetString()!;
    }
}
