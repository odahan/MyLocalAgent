using System.Text.Json;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Evaluation;

/// <summary>An explicit input and separate reference label; references never enter model prompts.</summary>
public sealed record EvaluationCase(string Id, string PairId, string Language, string Split, string Category,
    string Source, string Question, JsonElement Options, string Expected);

/// <summary>Loads reviewed paired fixtures or normalized external choice benchmarks.</summary>
public static class EvaluationCorpus
{
    /// <summary>Loads and validates every input without networking or inference.</summary>
    public static IReadOnlyList<EvaluationCase> Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported fixture schema.");
        var result = new List<EvaluationCase>();
        if (root.TryGetProperty("pairs", out var pairs))
        {
            foreach (var pair in pairs.EnumerateArray())
                foreach (var language in new[] { "fr", "en" })
                    result.Add(new(Text(pair, "id") + "-" + language, Text(pair, "id"), language,
                        Text(pair, "split"), Text(pair, "category"), Text(pair, language), Text(root, "question"),
                        root.GetProperty("options").Clone(), Text(pair, "expected")));
        }
        else
        {
            foreach (var row in root.GetProperty("cases").EnumerateArray())
                result.Add(new(Text(row, "id"), Text(row, "pairId"), Text(row, "language"), Text(row, "split"),
                    Text(row, "category"), Text(row, "source"), Text(row, "question"),
                    row.GetProperty("options").Clone(), Text(row, "expected")));
        }
        if (result.Count is < 1 or > 5000 || result.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidDataException("Expected 1-5000 uniquely identified cases.");
        foreach (var row in result)
        {
            DecisionEvaluateContract.Validate(JsonSerializer.SerializeToElement(new { source = row.Source, question = row.Question, options = row.Options }));
            if (!row.Options.TryGetProperty(row.Expected, out _)) throw new InvalidDataException("Unknown reference label: " + row.Id);
        }
        return result;
    }

    private static string Text(JsonElement json, string name) => json.GetProperty(name).GetString() is { Length: > 0 } text
        ? text : throw new InvalidDataException("Empty fixture field: " + name);
}
