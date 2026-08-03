namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one unresolved source span without retaining its prompt content.
/// </summary>
/// <param name="Kind">The program-owned warning category.</param>
/// <param name="Start">The zero-based UTF-16 source offset.</param>
/// <param name="Length">The UTF-16 source length.</param>
public sealed record AgentPromptPlaceholderOccurrence(
    AgentPromptPlaceholderWarningKind Kind,
    int Start,
    int Length);
