using System.Text.Json;
using System.Text.RegularExpressions;

namespace OD.MyLocalAgent.Core;

/// <summary>Validates authoring contracts before placeholder resolution can change classification policy.</summary>
public static class GmailDecisionContractValidator
{
    /// <summary>Validates literal policy fields and prevents previous output from authoring classification questions.</summary>
    public static void Validate(JsonElement args)
    {
        try
        {
            DecisionJson.Object(args, "selection", "daysBack", "maxMessages", "questions", "acceptance", "reportMapping", "arbitration", "unavailableBehavior", "batchTimeoutSeconds", "classificationMode");
            var questions = args.GetProperty("questions");
            foreach (var p in args.EnumerateObject())
            {
                if (p.Name == "questions") continue;
                if (p.Value.GetRawText().Contains("{{", StringComparison.Ordinal))
                    throw new WorkflowValidationException("Decision policies, enum values and report mappings must be literal, without placeholders.");
            }
            foreach (Match match in Regex.Matches(questions.GetRawText(), @"\{\{([^}]+)\}\}", RegexOptions.CultureInvariant))
            {
                var key = match.Groups[1].Value;
                if (key.Contains("output", StringComparison.OrdinalIgnoreCase) || key.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("foreach.", StringComparison.OrdinalIgnoreCase))
                    throw new WorkflowValidationException("Mail-derived workflow output cannot alter decision questions.");
            }
            var set = DecisionQuestionSet.Parse(questions);
            _ = DecisionAcceptancePolicy.Parse(set, new GmailDecisionOptions(),
                args.TryGetProperty("acceptance", out var a) ? a : default, args.GetProperty("reportMapping"),
                args.TryGetProperty("arbitration", out var b) ? b : default);
            var mode = args.TryGetProperty("classificationMode", out var modeValue) ? modeValue.GetString() : "Hybrid";
            if (mode is not ("Hybrid" or "LlmOnly")) throw new WorkflowValidationException("Unknown Gmail classification mode.");
            _ = mode == "LlmOnly" ? GmailDecisionProjection.LlmBudget(set) : GmailDecisionProjection.MailBudget(set);
            var selection = args.TryGetProperty("selection", out var s) ? s.GetString() : "Recent";
            if (selection is not ("Recent" or "Today" or "Unread") || (selection != "Recent" && args.TryGetProperty("daysBack", out _)))
                throw new WorkflowValidationException("daysBack is valid only for Recent selection.");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or NullReferenceException)
        { throw new WorkflowValidationException("Incomplete or invalid Gmail decision question/policy contract."); }
    }
}
