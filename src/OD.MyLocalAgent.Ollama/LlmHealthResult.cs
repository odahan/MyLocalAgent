namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Describes the health of a language-model provider and its configured model.
/// </summary>
/// <param name="IsReachable">Whether the provider endpoint is reachable.</param>
/// <param name="ModelAvailable">Whether the configured model is available.</param>
/// <param name="LatencyMs">The observed request latency in milliseconds.</param>
/// <param name="Error">An actionable error message when the check fails.</param>
public sealed record LlmHealthResult(
    bool IsReachable,
    bool ModelAvailable,
    long LatencyMs,
    string? Error);
