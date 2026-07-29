namespace OD.MyLocalAgent.Core;

/// <summary>
/// Describes one normative Tool Map entry.
/// </summary>
/// <param name="Alias">The case-insensitive workflow alias.</param>
/// <param name="AgentFunctionName">The agent-facing function name, when applicable.</param>
/// <param name="ToolGroup">The owning whitelist group.</param>
/// <param name="DeclaringType">The implementing type.</param>
/// <param name="MethodName">The implementing method name, when applicable.</param>
/// <param name="Arguments">The ordered camel-case argument names.</param>
/// <param name="SafetyFlag">The required safety flag, when applicable.</param>
/// <param name="IsAiStep">Whether the entry is an AI skill or prompt step.</param>
public sealed record ToolDefinition(
    string Alias,
    string? AgentFunctionName,
    string ToolGroup,
    Type DeclaringType,
    string? MethodName,
    IReadOnlyList<string> Arguments,
    string? SafetyFlag,
    bool IsAiStep)
{
    /// <summary>
    /// Gets a value indicating whether this entry is exposed as an agent tool.
    /// </summary>
    public bool IsAgentTool => AgentFunctionName is not null;
}
