namespace OD.MyLocalAgent.Evaluation;

/// <summary>Predeclared pilot criteria, assessed against held-out native observations without claiming production assurance.</summary>
public sealed record EvaluationCriteria(double MinAcceptedCoverage, double MaxWrongAcceptedRate, double MaxColdP95Ms, double MaxWarmP95Ms)
{
    /// <summary>Rejects meaningless or unbounded approval criteria before inference.</summary>
    public void Validate()
    {
        if (!double.IsFinite(MinAcceptedCoverage) || MinAcceptedCoverage is < 0 or > 1 ||
            !double.IsFinite(MaxWrongAcceptedRate) || MaxWrongAcceptedRate is < 0 or > 1 ||
            !double.IsFinite(MaxColdP95Ms) || MaxColdP95Ms <= 0 ||
            !double.IsFinite(MaxWarmP95Ms) || MaxWarmP95Ms <= 0)
            throw new ArgumentException("Pilot coverage/error criteria must be in [0,1] and latency bounds must be positive and finite.");
    }

    /// <summary>Separates failed criteria from missing evidence, including an unmeasured warm condition.</summary>
    public object Assess(IReadOnlyList<EvaluationObservation> observations)
    {
        Validate();
        var held = observations.Where(r => r.Backend == "native" && r.Split == "held-out").ToArray();
        var metrics = EvaluationAnalysis.Summarize(held, "native", "held-out", "all");
        var failures = new List<string>();
        var missing = new List<string>();
        if (held.Length == 0) missing.Add("No held-out native request-completeness observations; these criteria are not applied to general LLM comparators or unrelated public benchmarks.");
        else
        {
            if (metrics.Coverage < MinAcceptedCoverage) failures.Add("Accepted coverage is below the frozen minimum.");
            if (metrics.WrongAcceptedRate > MaxWrongAcceptedRate) failures.Add("Wrong accepted route rate exceeds the frozen maximum.");
            CheckLatency(true, MaxColdP95Ms, "cold");
            CheckLatency(false, MaxWarmP95Ms, "warm");
        }
        return new
        {
            conclusion = failures.Count > 0 ? "does-not-meet-pilot-criteria" : missing.Count > 0 ? "inconclusive" : "observed-pilot-criteria-met",
            failures, missingEvidence = missing, metrics, independentInputs = held.Select(r => r.CaseId).Distinct().Count(),
            independentPairs = held.Select(r => r.PairId).Distinct().Count(),
            limitation = "Repeated runs are correlated. A small held-out pilot is not a production reliability guarantee or sufficient evidence of benefit over baselines.",
        };

        void CheckLatency(bool cold, double bound, string name)
        {
            var values = held.Where(r => r.Cold == cold).Select(r => r.TotalMs).ToArray();
            if (values.Length == 0) missing.Add("No measured " + name + " native condition.");
            else if (EvaluationAnalysis.Percentile(values, .95) > bound) failures.Add("The " + name + " p95 latency exceeds the frozen bound.");
        }
    }
}
