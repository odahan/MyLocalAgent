namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Represents provider-reported token usage for one language-model call.
/// </summary>
/// <param name="Operation">The logical operation that initiated the call.</param>
/// <param name="InputTokenCount">The number of input tokens, if reported.</param>
/// <param name="OutputTokenCount">The number of output tokens, if reported.</param>
/// <param name="TotalTokenCount">The total number of tokens, if reported.</param>
public sealed record TaskLlmUsageEntry(
    string Operation,
    long? InputTokenCount,
    long? OutputTokenCount,
    long? TotalTokenCount);
