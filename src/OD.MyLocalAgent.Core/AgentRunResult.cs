namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents a stable application-level agent response.
/// </summary>
/// <param name="Text">The final agent report.</param>
/// <param name="ProviderName">The provider used for the execution.</param>
public sealed record AgentRunResult(string Text, string ProviderName);
