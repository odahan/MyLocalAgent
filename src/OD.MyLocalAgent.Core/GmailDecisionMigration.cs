using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Defines explicit compatibility checks without rewriting scheduled workflows.</summary>
public static class GmailDecisionMigration
{
    /// <summary>Rejects authored threshold weakening and retired arbitration choice rules in the version-two contract.</summary>
    internal static void ValidateAcceptance(JsonElement? json, GmailDecisionOptions settings)
    {
        if (json is null) return;
        var rules = DecisionContractJson.Read<AcceptanceContract>(json.Value);
        void Tightening(AcceptanceRules value)
        {
            if (value.MinTopProbabilityPercent < settings.MinTopProbabilityPercent ||
                value.MinMarginPercentagePoints < settings.MinMarginPercentagePoints ||
                value.MinConfidencePercent < settings.MinConfidencePercent ||
                value.MinNoulCertaintyPercent < settings.MinNoulCertaintyPercent)
                throw new WorkflowValidationException("Native Gmail acceptance may only tighten global Settings thresholds.");
        }
        Tightening(rules);
        foreach (var question in rules.PerQuestion.Values)
        {
            Tightening(question);
            if (question.AlwaysArbitrateChoices.Count > 0)
                throw new WorkflowValidationException("Retired alwaysArbitrateChoices requires deliberate migration to semantic review routing.");
            foreach (var choice in question.PerChoice.Values) Tightening(choice);
        }
    }
    /// <summary>Maps nonconflicting legacy selectors and rejects secondary-engine policies before collection.</summary>
    public static void Validate(string mode, JsonElement? arbitration, string? unavailableBehavior, string engine, JsonElement? acceptance)
    {
        if (mode is not ("Global" or "Hybrid" or "LlmOnly"))
            throw new WorkflowValidationException("Unknown Gmail classification mode.");
        if ((mode == "Hybrid" && engine != "NativeDecision") || (mode == "LlmOnly" && engine != "CentralLlm"))
            throw new WorkflowValidationException("Legacy Gmail classificationMode conflicts with the global engine. Remove classificationMode to migrate to the global contract; scheduled tasks are never rewritten automatically.");
        if (arbitration is not null || unavailableBehavior is not null)
            throw new WorkflowValidationException("Remove legacy arbitration and unavailableBehavior explicitly to migrate. Global decisions never request a second opinion or fallback.");
        if (engine == "CentralLlm" && acceptance is not null)
            throw new WorkflowValidationException("Explicit native Gmail acceptance cannot be satisfied by the central LLM. Remove acceptance deliberately or select the native engine.");
    }
}
