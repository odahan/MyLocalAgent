using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Evaluation;

/// <summary>A checkpointed observed result, retaining uncertainty and unavailability independently of semantic labels.</summary>
public sealed record EvaluationObservation(string CaseId, string PairId, string Language, string Split,
    string Category, int Repetition, string Backend, string Expected, string? Choice, string Status,
    IReadOnlyDictionary<string, double>? Probabilities, double? Confidence, double GateWaitMs,
    double InferenceMs, double TotalMs, bool? Cold, IReadOnlyList<string> Reasons)
{
    /// <summary>Links a standalone observation to its frozen run manifest.</summary>
    public string RunId { get; init; } = string.Empty;
    /// <summary>Records the resolved model digest, or the frozen rule implementation hash.</summary>
    public string ModelDigest { get; init; } = string.Empty;
    /// <summary>Links each result to its exact sanitized configuration.</summary>
    public string ConfigurationSha256 { get; init; } = string.Empty;
}

/// <summary>Auditable counts and denominators for one model, partition and language.</summary>
public sealed record EvaluationMetrics(string Backend, string Split, string Language, int Count, int Scored,
    int Correct, int Accepted, int WrongAccepted, int Uncertain, int Unavailable, double? ChoiceAccuracy,
    double Coverage, double? AcceptedAccuracy, double WrongAcceptedRate, double MedianMs, double P95Ms,
    IReadOnlyDictionary<string, int> Confusion);

/// <summary>Produces offline comparative summaries without altering thresholds or reference annotations.</summary>
public static class EvaluationAnalysis
{
    /// <summary>Computes choice agreement, accepted risk and nearest-rank latency percentiles.</summary>
    public static EvaluationMetrics Summarize(IEnumerable<EvaluationObservation> observations, string backend, string split, string language)
    {
        var rows = observations.Where(r => r.Backend == backend && (split == "all" || r.Split == split) &&
            (language == "all" || r.Language == language)).ToArray();
        var scored = rows.Where(r => r.Choice is not null && r.Status != "unavailable").ToArray();
        var accepted = rows.Where(r => r.Status == "accepted").ToArray();
        var wrong = accepted.Count(r => r.Choice != r.Expected);
        var correct = scored.Count(r => r.Choice == r.Expected);
        return new(backend, split, language, rows.Length, scored.Length, correct, accepted.Length, wrong,
            rows.Count(r => r.Status == "uncertain"), rows.Count(r => r.Status == "unavailable"),
            scored.Length == 0 ? null : (double)correct / scored.Length,
            rows.Length == 0 ? 0 : (double)accepted.Length / rows.Length,
            accepted.Length == 0 ? null : (double)(accepted.Length - wrong) / accepted.Length,
            rows.Length == 0 ? 0 : (double)wrong / rows.Length, Percentile(rows.Select(r => r.TotalMs), .5),
            Percentile(rows.Select(r => r.TotalMs), .95), scored.GroupBy(r => r.Expected + " -> " + r.Choice)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
    }

    /// <summary>Uses the nearest-rank definition and records no invented value for empty data.</summary>
    public static double Percentile(IEnumerable<double> values, double quantile)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(quantile * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    /// <summary>Reapplies a candidate acceptance threshold to valid native scores, retaining ties and concentration policy.</summary>
    public static bool AcceptedAt(EvaluationObservation row, double top, double margin, double concentration)
    {
        if (row.Probabilities is null || row.Status == "unavailable") return false;
        var ordered = row.Probabilities.Values.OrderDescending().ToArray();
        return ordered.Length >= 2 && ordered[0] > ordered[1] && ordered[0] >= top &&
            ordered[0] - ordered[1] >= margin && (concentration == 0 || row.Confidence >= concentration);
    }

    /// <summary>Writes machine-readable counts and a concise report with explicit limitations and threshold sweeps.</summary>
    public static void Write(string directory, IReadOnlyList<EvaluationObservation> rows, double concentration)
    {
        var metrics = rows.Select(r => r.Backend).Distinct().SelectMany(backend =>
            new[] { "all" }.Concat(rows.Select(r => r.Split).Distinct()).SelectMany(split =>
                new[] { "all" }.Concat(rows.Select(r => r.Language).Distinct()).Select(language => Summarize(rows, backend, split, language)))).ToArray();
        var sweepSplit = rows.Any(r => r.Split == "held-out") ? "held-out" : rows.Any(r => r.Split == "public") ? "public" : "development";
        var sweeps = new[] { .8, .9, .95 }.Where(_ => rows.Any(r => r.Backend == "native"))
            .SelectMany(top => new[] { .2, .3, .5 }.Select(margin =>
        {
            var native = rows.Where(r => r.Backend == "native").Select(r => r with
            { Status = r.Status == "unavailable" ? r.Status : AcceptedAt(r, top, margin, concentration) ? "accepted" : "uncertain" }).ToArray();
            return new { top, margin, metrics = Summarize(native, "native", sweepSplit, "all") };
        })).ToArray();
        var paired = rows.Where(r => r.Language is "fr" or "en").GroupBy(r => (r.Backend, r.PairId, r.Repetition))
            .Where(g => g.Count() == 2 && g.Select(r => r.Language).Distinct().Count() == 2)
            .Select(g => new { g.Key.Backend, g.Key.PairId, g.Key.Repetition, choicesDisagree = g.Select(r => r.Choice).Distinct().Count() > 1,
                statusesDisagree = g.Select(r => r.Status).Distinct().Count() > 1 }).ToArray();
        var latency = rows.Where(r => r.Backend != "rule").GroupBy(r => (r.Backend, r.Cold)).Select(g => new
        { g.Key.Backend, g.Key.Cold, count = g.Count(), medianMs = Percentile(g.Select(r => r.TotalMs), .5), p95Ms = Percentile(g.Select(r => r.TotalMs), .95) }).ToArray();
        var categories = rows.GroupBy(r => (r.Backend, r.Split, r.Category)).Select(g => new
        { g.Key.Backend, g.Key.Split, g.Key.Category, count = g.Count(), wrongAccepted = g.Count(r => r.Status == "accepted" && r.Choice != r.Expected) }).ToArray();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var analysisAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(EvaluationAnalysis).Assembly.Location)));
        File.WriteAllText(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new
        { analyzedUtc = DateTimeOffset.UtcNow, analysisAssemblySha256, metrics, latency, paired, categories, sweeps }, options));
        var report = new StringBuilder("# Decision evaluation observations\n\nPercentiles use nearest rank. Accuracy is reference-label agreement among scored results; coverage and wrong accepted rate use all attempted inputs. Unavailable results are counted separately.\n\n| Backend | Partition | Language | N | Choice accuracy | Accepted | Wrong accepted | Uncertain | Unavailable | p95 total ms |\n| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var row in metrics.Where(m => m.Count > 0))
            report.AppendLine(FormattableString.Invariant($"| {row.Backend} | {row.Split} | {row.Language} | {row.Count} | {(row.ChoiceAccuracy is null ? "n/a" : (row.ChoiceAccuracy * 100).Value.ToString("0.##", CultureInfo.InvariantCulture) + "%")} | {row.Accepted} | {row.WrongAccepted} | {row.Uncertain} | {row.Unavailable} | {row.P95Ms:0.##} |"));
        report.AppendLine("\nSee summary.json for confusion counts, paired disagreements, category breakdowns, cold/warm timings and threshold sweeps. These are pilot observations. Repetitions are correlated and do not enlarge the independent sample. Zero observed errors do not establish zero risk. Public adapted inputs are not an exact replication of vendor results. A rule baseline is limited to the appointment policy; no probabilities are invented for it or the general LLM.");
        File.WriteAllText(Path.Combine(directory, "report.md"), report.ToString());
    }
}
