using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Validates the simple choice contract before inference or placeholder resolution.</summary>
public static class DecisionEvaluateContract
{
    /// <summary>Validates authored workflow fields, allowing content placeholders only in source.</summary>
    public static void Validate(JsonElement args)
    {
        try
        {
            DecisionJson.Object(args, "source", "question", "options", "acceptance");
            _ = DecisionJson.Text(args.GetProperty("source"), int.MaxValue);
            foreach (var property in args.EnumerateObject())
                if (property.Name != "source" && property.Value.GetRawText().Contains("{{", StringComparison.Ordinal))
                    throw new WorkflowValidationException("Decision questions, options and acceptance must be literal.");
            _ = Questions(args.GetProperty("question").GetString()!, args.GetProperty("options"));
            _ = Acceptance(args.TryGetProperty("acceptance", out var acceptance) ? acceptance : null,
                new() { MinTopProbabilityPercent = 0, MinMarginPercentagePoints = 0 });
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        { throw new WorkflowValidationException("Incomplete or invalid decision evaluation contract."); }
    }

    internal static DecisionQuestionSet Questions(string question, JsonElement options)
    {
        DecisionJson.Object(options);
        return DecisionQuestionSet.Parse(JsonSerializer.SerializeToElement(new
        {
            decision = new { type = "choice", instructions = question, criteria = options },
        }));
    }

    /// <summary>Resolves normalized acceptance overrides against the configured minimums.</summary>
    public static DecisionAcceptance Acceptance(JsonElement? json, DecisionOptions settings)
    {
        var authored = json is null ? new DecisionAcceptanceOverrides() : DecisionContractJson.Read<DecisionAcceptanceOverrides>(json.Value);
        var top = settings.MinTopProbabilityPercent / 100;
        var margin = settings.MinMarginPercentagePoints / 100;
        if (authored.MinTopProbability is { } t && (!double.IsFinite(t) || t < top || t > 1) ||
            authored.MinMargin is { } m && (!double.IsFinite(m) || m < margin || m > 1))
            throw new WorkflowValidationException("Acceptance overrides must be in [0, 1] and may only tighten configured defaults.");
        return new(authored.MinTopProbability ?? top, authored.MinMargin ?? margin, settings.MinConfidencePercent / 100);
    }
}

internal sealed record DecisionAcceptanceOverrides
{
    public double? MinTopProbability { get; init; }
    public double? MinMargin { get; init; }
}

/// <summary>Effective inclusive acceptance thresholds in normalized units.</summary>
public sealed record DecisionAcceptance(double MinTopProbability, double MinMargin, double MinConfidence);
