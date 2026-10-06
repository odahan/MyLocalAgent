using System.Text.Json;
using System.Text.Json.Serialization;

namespace OD.MyLocalAgent.Core;

/// <summary>Deterministic acceptance and arbitration mathematics, using unrounded native scores.</summary>
public sealed class DecisionAcceptancePolicy
{
    internal AcceptanceRules Defaults { get; }
    internal Dictionary<string, QuestionAcceptance> PerQuestion { get; }
    internal ReportMapping Mapping { get; }
    internal ArbitrationRules Arbitration { get; }
    private readonly DecisionQuestionSet _questions;

    private DecisionAcceptancePolicy(DecisionQuestionSet questions, AcceptanceRules defaults,
        Dictionary<string, QuestionAcceptance> perQuestion, ReportMapping mapping, ArbitrationRules arbitration)
    {
        _questions = questions;
        Defaults = defaults;
        PerQuestion = perQuestion;
        Mapping = mapping;
        Arbitration = arbitration;
    }

    internal static DecisionAcceptancePolicy Parse(DecisionQuestionSet questions, GmailDecisionOptions settings,
        JsonElement acceptance, JsonElement reportMapping, JsonElement arbitration)
    {
        var authored = acceptance.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? new AcceptanceContract() : DecisionContractJson.Read<AcceptanceContract>(acceptance);
        var defaults = new AcceptanceRules
        {
            MinTopProbabilityPercent = authored.MinTopProbabilityPercent ?? settings.MinTopProbabilityPercent,
            MinMarginPercentagePoints = authored.MinMarginPercentagePoints ?? settings.MinMarginPercentagePoints,
            MinConfidencePercent = authored.MinConfidencePercent ?? settings.MinConfidencePercent,
            MinNoulCertaintyPercent = authored.MinNoulCertaintyPercent ?? settings.MinNoulCertaintyPercent,
        };
        ValidateRules(defaults, null);
        var mapping = DecisionContractJson.Read<ReportMapping>(reportMapping);
        RequireType(questions, mapping.CategoryQuestion, "choice");
        if (mapping.ReplyQuestion is not null) RequireType(questions, mapping.ReplyQuestion, "noul");
        if (mapping.PriorityQuestion is not null) RequireType(questions, mapping.PriorityQuestion, "score");
        var category = questions.Questions[mapping.CategoryQuestion];
        if (mapping.Categories.Count != category.Keys.Count || mapping.Categories.Keys.Any(k => !category.Keys.Contains(k)) ||
            mapping.Categories.Values.Any(c => string.IsNullOrWhiteSpace(c.Label) || c.Label.Length > 100 || c.Label.Contains("{{", StringComparison.Ordinal) || c.AttentionRank is < 0 or > 100) ||
            !ValidPercent(mapping.ReplyTrueAtPercent))
        {
            throw new WorkflowValidationException("Report categories must map every configured choice to a bounded label and attention rank.");
        }
        foreach (var (name, rule) in authored.PerQuestion)
        {
            if (!questions.Questions.TryGetValue(name, out var q))
                throw new WorkflowValidationException("Acceptance references an unknown question.");
            ValidateRules(rule, q.Type);
            if (q.Type != "choice" && (rule.PerChoice.Count != 0 || rule.AlwaysArbitrateChoices.Count != 0))
                throw new WorkflowValidationException("Per-choice rules apply only to choice questions.");
            if (rule.AlwaysArbitrateChoices.Distinct(StringComparer.OrdinalIgnoreCase).Count() != rule.AlwaysArbitrateChoices.Count ||
                rule.AlwaysArbitrateChoices.Any(c => !q.Keys.Contains(c)))
                throw new WorkflowValidationException("Always-arbitrate choices must be unique configured choice keys.");
            foreach (var (key, choiceRule) in rule.PerChoice)
            {
                if (!q.Keys.Contains(key)) throw new WorkflowValidationException("Acceptance references an unknown choice.");
                ValidateRules(choiceRule, "choice");
            }
        }
        var a = arbitration.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? new ArbitrationRules() : DecisionContractJson.Read<ArbitrationRules>(arbitration);
        a = a with
        {
            Enabled = a.Enabled ?? settings.ArbitrationEnabled,
            MaxPercent = a.MaxPercent ?? settings.MaxArbitrationPercent,
            MaxCalls = a.MaxCalls ?? settings.MaxArbitrationCalls,
        };
        if (a.MaxPercent is < 0 or > 100 || a.MaxCalls is < 0 or > 500 || a.Conflicts.Count > 16)
            throw new WorkflowValidationException("Invalid second-opinion budget or conflict limit.");
        foreach (var conflict in a.Conflicts)
        {
            RequireType(questions, conflict.ChoiceQuestion, "choice");
            if (!questions.Questions[conflict.ChoiceQuestion].Keys.Contains(conflict.Choice))
                throw new WorkflowValidationException("Conflict references an unknown choice.");
            if (conflict.NoulQuestion is not null && conflict.ScoreQuestion is null && conflict.NoulAtLeastPercent is { } n &&
                ValidPercent(n) && conflict.ScoreLevel is null && conflict.ScoreLevelAtLeastPercent is null)
                RequireType(questions, conflict.NoulQuestion, "noul");
            else if (conflict.ScoreQuestion is not null && conflict.NoulQuestion is null && conflict.NoulAtLeastPercent is null &&
                     conflict.ScoreLevel is { } level && conflict.ScoreLevelAtLeastPercent is { } s && ValidPercent(s))
            {
                RequireType(questions, conflict.ScoreQuestion, "score");
                if (level < 0 || level >= questions.Questions[conflict.ScoreQuestion].Keys.Count)
                    throw new WorkflowValidationException("Conflict rubric level is out of range.");
            }
            else throw new WorkflowValidationException("A conflict requires exactly one typed noul or rubric probability threshold.");
        }
        return new DecisionAcceptancePolicy(questions, defaults, authored.PerQuestion, mapping, a);
    }

    /// <summary>Calculates the exact attempted second-opinion cap, rounding percentages down.</summary>
    public static int ArbitrationCap(int count, int maxPercent, int maxCalls)
    {
        if (count is < 0 or > 500 || maxPercent is < 0 or > 100 || maxCalls is < 0 or > 500)
            throw new WorkflowValidationException("Invalid arbitration message count or budget.");
        return Math.Min(count, Math.Min((int)((long)count * maxPercent / 100), maxCalls > 0 ? maxCalls : count));
    }

    internal List<string> Evaluate(DecisionScores result, bool currentContentTruncated)
    {
        var reasons = new List<string>();
        foreach (var (name, score) in result.Scores)
        {
            PerQuestion.TryGetValue(name, out var questionRule);
            AcceptanceRules? choiceRule = null;
            if (score.Choice is { } choice && questionRule is not null) questionRule.PerChoice.TryGetValue(choice, out choiceRule);
            double Effective(Func<AcceptanceRules, double?> select) =>
                (choiceRule is null ? null : select(choiceRule)) ?? (questionRule is null ? null : select(questionRule)) ?? select(Defaults)!.Value;
            if (_questions.Questions[name].Type == "noul")
            {
                if (100 * Math.Max(score.Noul!.Value, 1 - score.Noul.Value) < Effective(r => r.MinNoulCertaintyPercent))
                    reasons.Add("NoulCertaintyBelowThreshold");
                continue;
            }
            if (score.Tied) reasons.Add("TiedTopScores");
            if (score.Top * 100 < Effective(r => r.MinTopProbabilityPercent)) reasons.Add("TopProbabilityBelowThreshold");
            if (score.Margin * 100 < Effective(r => r.MinMarginPercentagePoints)) reasons.Add("MarginBelowThreshold");
            var concentration = Effective(r => r.MinConfidencePercent);
            if (concentration > 0 && (score.Confidence is null || score.Confidence * 100 < concentration)) reasons.Add("ConcentrationBelowThreshold");
            if (score.Choice is { } c && questionRule?.AlwaysArbitrateChoices.Contains(c) == true) reasons.Add("AlwaysArbitrateChoice");
        }
        foreach (var conflict in Arbitration.Conflicts)
        {
            if (!Equals(result.Answers[conflict.ChoiceQuestion], conflict.Choice)) continue;
            if ((conflict.NoulQuestion is { } n && result.Scores[n].Noul * 100 >= conflict.NoulAtLeastPercent) ||
                (conflict.ScoreQuestion is { } s && result.Scores[s].Probabilities![conflict.ScoreLevel!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)] * 100 >= conflict.ScoreLevelAtLeastPercent))
                reasons.Add("CrossQuestionConflict");
        }
        if (currentContentTruncated) reasons.Add("CurrentContentTruncated");
        return reasons.Distinct(StringComparer.Ordinal).ToList();
    }

    internal bool RemainsUncertain(Dictionary<string, object> answers) => PerQuestion.Any(p =>
        answers.TryGetValue(p.Key, out var v) && v is string s && p.Value.AlwaysArbitrateChoices.Contains(s));

    private static void RequireType(DecisionQuestionSet questions, string name, string type)
    {
        if (!questions.Questions.TryGetValue(name, out var q) || q.Type != type)
            throw new WorkflowValidationException("Report mapping or conflict references an unknown or incompatible question.");
    }

    private static bool ValidPercent(double v) => double.IsFinite(v) && v is >= 0 and <= 100;
    private static void ValidateRules(AcceptanceRules rules, string? type)
    {
        if (new[] { rules.MinTopProbabilityPercent, rules.MinMarginPercentagePoints, rules.MinConfidencePercent, rules.MinNoulCertaintyPercent }
            .Any(v => v.HasValue && !ValidPercent(v.Value)) || rules.MinNoulCertaintyPercent < 50)
            throw new WorkflowValidationException("Acceptance percentages are outside their valid range.");
        if ((type == "noul" && (rules.MinTopProbabilityPercent.HasValue || rules.MinMarginPercentagePoints.HasValue || rules.MinConfidencePercent.HasValue)) ||
            (type is "choice" or "score" && rules.MinNoulCertaintyPercent.HasValue))
            throw new WorkflowValidationException("Acceptance properties do not match the question type.");
    }
}

internal record AcceptanceRules
{
    public double? MinTopProbabilityPercent { get; init; }
    public double? MinMarginPercentagePoints { get; init; }
    public double? MinConfidencePercent { get; init; }
    public double? MinNoulCertaintyPercent { get; init; }
}
internal sealed record AcceptanceContract : AcceptanceRules
{
    public Dictionary<string, QuestionAcceptance> PerQuestion { get; init; } = [];
}
internal sealed record QuestionAcceptance : AcceptanceRules
{
    public List<string> AlwaysArbitrateChoices { get; init; } = [];
    public Dictionary<string, AcceptanceRules> PerChoice { get; init; } = [];
}
internal sealed record ReportCategory([property: JsonRequired] string Label, [property: JsonRequired] int AttentionRank);
internal sealed record ReportMapping
{
    public string CategoryQuestion { get; init; } = "";
    public string? ReplyQuestion { get; init; }
    public string? PriorityQuestion { get; init; }
    public double ReplyTrueAtPercent { get; init; } = 50;
    public Dictionary<string, ReportCategory> Categories { get; init; } = [];
}
internal sealed record ArbitrationRules
{
    public bool? Enabled { get; init; }
    public int? MaxPercent { get; init; }
    public int? MaxCalls { get; init; }
    public List<DecisionConflict> Conflicts { get; init; } = [];
}
internal sealed record DecisionConflict
{
    public string ChoiceQuestion { get; init; } = "";
    public string Choice { get; init; } = "";
    public string? NoulQuestion { get; init; }
    public double? NoulAtLeastPercent { get; init; }
    public string? ScoreQuestion { get; init; }
    public int? ScoreLevel { get; init; }
    public double? ScoreLevelAtLeastPercent { get; init; }
}
internal static class DecisionContractJson
{
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static T Read<T>(JsonElement json)
    {
        ValidateDuplicates(json);
        try { return json.Deserialize<T>(Options) ?? throw new WorkflowValidationException("Decision contract cannot be null."); }
        catch (JsonException) { throw new WorkflowValidationException("Invalid or unknown decision contract properties."); }
    }

    private static void ValidateDuplicates(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            DecisionJson.Object(json);
            foreach (var p in json.EnumerateObject()) ValidateDuplicates(p.Value);
        }
        else if (json.ValueKind == JsonValueKind.Array)
            foreach (var item in json.EnumerateArray()) ValidateDuplicates(item);
    }
}
