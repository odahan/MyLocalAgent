namespace OD.MyLocalAgent.Core;

/// <summary>Represents one visible answer or reasoning update from an agent run.</summary>
public sealed record AgentStreamUpdate(string Text, bool IsReasoning);
