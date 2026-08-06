namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Represents a single timed operation recorded during a task execution.
/// </summary>
/// <param name="Category">The stable category, e.g. <c>ai.prompt</c>, <c>tool</c>, or <c>guardrail</c>.</param>
/// <param name="Name">The operation name, e.g. the tool alias or the guardrail operation.</param>
/// <param name="Duration">The measured wall-clock duration.</param>
public sealed record TaskTimingEntry(string Category, string Name, TimeSpan Duration);
