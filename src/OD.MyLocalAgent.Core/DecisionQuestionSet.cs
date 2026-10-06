using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OD.MyLocalAgent.Core;

/// <summary>Strict, ordered classification questions and typed local answer validation.</summary>
public sealed class DecisionQuestionSet
{
    private readonly Dictionary<string, DecisionQuestion> _questions;
    internal IReadOnlyDictionary<string, DecisionQuestion> Questions => _questions;
    /// <summary>Gets the immutable authored question document, preserving rubric order.</summary>
    public JsonElement Json { get; }

    private DecisionQuestionSet(JsonElement json, Dictionary<string, DecisionQuestion> questions)
    {
        Json = json.Clone();
        _questions = questions;
    }

    /// <summary>Validates question names, descriptions, types, collisions, and byte limits before collection.</summary>
    public static DecisionQuestionSet Parse(JsonElement json)
    {
        DecisionJson.Object(json);
        if (Encoding.UTF8.GetByteCount(json.GetRawText()) > 24 * 1024)
        {
            throw new WorkflowValidationException("Decision questions exceed 24 KiB.");
        }

        var questions = new Dictionary<string, DecisionQuestion>(StringComparer.Ordinal);
        foreach (var p in json.EnumerateObject())
        {
            Identifier(p.Name);
            DecisionJson.Object(p.Value, "type", "instructions", "criteria");
            var type = DecisionJson.Text(p.Value.GetProperty("type"), 16);
            var instructions = DecisionJson.Text(p.Value.GetProperty("instructions"), 1000);
            var keys = new List<string>();
            if (type == "score")
            {
                var criteria = p.Value.GetProperty("criteria");
                if (criteria.ValueKind != JsonValueKind.Array || criteria.GetArrayLength() is < 2 or > 10)
                {
                    throw new WorkflowValidationException("Score questions require 2–10 ordered rubric levels.");
                }
                foreach (var item in criteria.EnumerateArray())
                {
                    _ = DecisionJson.Text(item, 400);
                    keys.Add(keys.Count.ToString(CultureInfo.InvariantCulture));
                }
            }
            else if (type == "choice")
            {
                var criteria = p.Value.GetProperty("criteria");
                DecisionJson.Object(criteria);
                foreach (var item in criteria.EnumerateObject())
                {
                    Identifier(item.Name);
                    _ = DecisionJson.Text(item.Value, 400);
                    keys.Add(item.Name);
                }
                if (keys.Count is < 2 or > 24)
                {
                    throw new WorkflowValidationException("Choice questions require 2–24 named answers.");
                }
            }
            else if (type == "noul")
            {
                if (p.Value.TryGetProperty("criteria", out var criteria))
                {
                    DecisionJson.Object(criteria, "true", "false");
                    _ = DecisionJson.Text(criteria.GetProperty("true"), 400);
                    _ = DecisionJson.Text(criteria.GetProperty("false"), 400);
                }
            }
            else
            {
                throw new WorkflowValidationException("Question type must be choice, noul, or score.");
            }

            questions.Add(p.Name, new DecisionQuestion(type, instructions, keys));
        }

        if (questions.Count is < 1 or > 8)
        {
            throw new WorkflowValidationException("Decision questions require 1–8 unique named entries.");
        }
        return new DecisionQuestionSet(json, questions);
    }

    internal Dictionary<string, object> ValidateLlmAnswers(JsonElement answers)
    {
        DecisionJson.Object(answers, _questions.Keys.ToArray());
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (name, q) in _questions)
        {
            if (!answers.TryGetProperty(name, out var answer))
            {
                throw new DecisionResponseException("MissingAnswer");
            }
            values.Add(name, q.Type switch
            {
                "choice" when answer.ValueKind == JsonValueKind.String && q.Keys.Contains(answer.GetString()!) => answer.GetString()!,
                "noul" when answer.ValueKind is JsonValueKind.True or JsonValueKind.False => answer.GetBoolean(),
                "score" when answer.ValueKind == JsonValueKind.Number && answer.TryGetInt32(out var level) && level >= 0 && level < q.Keys.Count => level,
                _ => throw new DecisionResponseException("UnknownOption"),
            });
        }
        return values;
    }

    /// <summary>Validates a synthetic native provider response without exposing source or internal scores.</summary>
    public void ValidateResponse(JsonElement payload) => _ = ValidateNative(payload, 50);

    internal DecisionScores ValidateNative(JsonElement payload, double replyThreshold)
    {
        DecisionJson.Object(payload, "model", "answers", "usage");
        if (!payload.TryGetProperty("answers", out var answers))
        {
            throw new DecisionResponseException("MissingAnswer");
        }
        DecisionJson.Object(answers, _questions.Keys.ToArray());
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var scores = new Dictionary<string, NativeDecisionScore>(StringComparer.Ordinal);
        foreach (var (name, q) in _questions)
        {
            if (!answers.TryGetProperty(name, out var answer))
            {
                throw new DecisionResponseException("MissingAnswer");
            }
            DecisionJson.Object(answer, q.Type switch
            {
                "noul" => ["type", "noul"],
                "choice" => ["type", "choice", "probabilities", "confidence"],
                _ => ["type", "score", "legend", "probabilities", "confidence"],
            });
            if (answer.GetProperty("type").GetString() != q.Type)
            {
                throw new DecisionResponseException("IncompatibleAnswerType");
            }
            if (q.Type == "noul")
            {
                var n = DecisionJson.Unit(answer.GetProperty("noul"));
                values.Add(name, n * 100 >= replyThreshold);
                scores.Add(name, new NativeDecisionScore { Noul = n });
                continue;
            }

            var probabilities = answer.GetProperty("probabilities");
            DecisionJson.Object(probabilities, q.Keys.ToArray());
            var distribution = probabilities.EnumerateObject().ToDictionary(p => p.Name, p => DecisionJson.Unit(p.Value));
            if (distribution.Count != q.Keys.Count || Math.Abs(distribution.Values.Sum() - 1) > 0.01)
            {
                throw new DecisionResponseException("InvalidProbabilityDistribution");
            }
            var sorted = distribution.OrderByDescending(p => p.Value).ToArray();
            var selected = q.Type == "choice" ? answer.GetProperty("choice").GetString()! : sorted[0].Key;
            if (!distribution.TryGetValue(selected, out var selectedScore) || selectedScore != sorted[0].Value)
            {
                throw new DecisionResponseException("SelectedTopMismatch");
            }
            double? weighted = null;
            if (q.Type == "score")
            {
                weighted = answer.GetProperty("score").GetDouble();
                var expected = distribution.Sum(p => int.Parse(p.Key, CultureInfo.InvariantCulture) * p.Value);
                if (!double.IsFinite(weighted.Value) || weighted < 0 || weighted > q.Keys.Count - 1 || Math.Abs(expected - weighted.Value) > 0.01)
                {
                    throw new DecisionResponseException("InvalidRubricScore");
                }
            }
            var confidence = answer.TryGetProperty("confidence", out var c) ? DecisionJson.Unit(c) : (double?)null;
            values.Add(name, q.Type == "choice" ? selected : int.Parse(selected, CultureInfo.InvariantCulture));
            scores.Add(name, new NativeDecisionScore
            {
                Choice = q.Type == "choice" ? selected : null,
                Level = q.Type == "score" ? int.Parse(selected, CultureInfo.InvariantCulture) : null,
                Score = weighted,
                Probabilities = distribution,
                Confidence = confidence,
                Top = sorted[0].Value,
                Margin = sorted[0].Value - sorted[1].Value,
                Tied = sorted[0].Value == sorted[1].Value,
            });
        }
        return new DecisionScores(values, scores);
    }

    internal JsonElement LlmSchema()
    {
        var properties = _questions.ToDictionary(p => p.Key, p => (object)(p.Value.Type switch
        {
            "choice" => new { type = "string", @enum = (object)p.Value.Keys },
            "noul" => (object)new { type = "boolean" },
            _ => new { type = "integer", minimum = 0, maximum = p.Value.Keys.Count - 1 },
        }));
        return JsonSerializer.SerializeToElement(new { type = "object", properties, required = _questions.Keys, additionalProperties = false });
    }

    private static void Identifier(string name)
    {
        if (!Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant))
        {
            throw new WorkflowValidationException("Question and choice keys must be literal identifiers of at most 64 characters.");
        }
    }
}

internal sealed record DecisionQuestion(string Type, string Instructions, IReadOnlyList<string> Keys);
internal sealed record DecisionScores(Dictionary<string, object> Answers, Dictionary<string, NativeDecisionScore> Scores);
internal sealed record NativeDecisionScore
{
    public string? Choice { get; init; }
    public int? Level { get; init; }
    public double? Noul { get; init; }
    public double? Score { get; init; }
    public Dictionary<string, double>? Probabilities { get; set; }
    public double? Confidence { get; init; }
    public double Top { get; init; }
    public double Margin { get; init; }
    public bool Tied { get; init; }
}

internal sealed class DecisionResponseException(string code) : Exception($"Invalid local decision response ({code}).")
{
    public string Code { get; } = code;
}

internal static class DecisionJson
{
    internal static void Object(JsonElement json, params string[] allowed)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new WorkflowValidationException("Expected a decision contract object.");
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in json.EnumerateObject())
        {
            if (!seen.Add(p.Name) || (allowed.Length > 0 && !allowed.Contains(p.Name, StringComparer.Ordinal)))
            {
                throw new WorkflowValidationException("Duplicate or unknown decision contract property.");
            }
        }
    }

    internal static string Text(JsonElement json, int maximum)
    {
        if (json.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(json.GetString()) || json.GetString()!.Length > maximum)
        {
            throw new WorkflowValidationException($"Expected non-empty decision text of at most {maximum} characters.");
        }
        return json.GetString()!;
    }

    internal static double Unit(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Number || !json.TryGetDouble(out var value) || !double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new DecisionResponseException("InvalidProbability");
        }
        return value;
    }
}
