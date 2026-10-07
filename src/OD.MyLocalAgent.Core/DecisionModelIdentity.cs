namespace OD.MyLocalAgent.Core;

/// <summary>Shares installed-model and endpoint identity comparisons across native callers.</summary>
internal static class DecisionModelIdentity
{
    internal static bool TryGet(IReadOnlyDictionary<string, string> models, string model, out string? identity) =>
        models.TryGetValue(model.Contains(':') ? model : model + ":latest", out identity) || models.TryGetValue(model, out identity);

    internal static bool SameEndpoint(string a, string b) =>
        Ollama.LocalDecisionEndpoint.Identity(a) == Ollama.LocalDecisionEndpoint.Identity(b);
}
