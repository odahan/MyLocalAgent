using System.Text.Json;
using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes the whitelisted runtime view of the normative Tool Map.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets all normative Tool Map definitions.
    /// </summary>
    IReadOnlyList<ToolDefinition> Definitions { get; }

    /// <summary>
    /// Builds the agent tools allowed by the supplied tool-group whitelist.
    /// </summary>
    /// <param name="enabledToolGroups">The task tool-group whitelist.</param>
    /// <returns>The whitelisted agent tools.</returns>
    AITool[] GetAgentTools(IEnumerable<string> enabledToolGroups);

    /// <summary>
    /// Builds the agent tools allowed by the supplied whitelist and execution context.
    /// </summary>
    /// <param name="enabledToolGroups">The task tool-group whitelist.</param>
    /// <param name="executionContext">The immutable tool execution context.</param>
    /// <returns>The contextual whitelisted agent tools.</returns>
    AITool[] GetAgentTools(
        IEnumerable<string> enabledToolGroups,
        ToolExecutionContext executionContext);

    /// <summary>
    /// Resolves a Tool Map alias.
    /// </summary>
    /// <param name="alias">The workflow alias.</param>
    /// <param name="definition">The resolved definition.</param>
    /// <returns><see langword="true"/> when the alias exists.</returns>
    bool TryGetDefinition(string alias, out ToolDefinition? definition);

    /// <summary>
    /// Invokes a whitelisted tool alias using JSON arguments.
    /// </summary>
    /// <param name="alias">The workflow alias.</param>
    /// <param name="arguments">The JSON argument object.</param>
    /// <param name="enabledToolGroups">The task tool-group whitelist.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The compact JSON tool result.</returns>
    Task<string> InvokeAsync(
        string alias,
        JsonElement arguments,
        IEnumerable<string> enabledToolGroups,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invokes a whitelisted tool alias with an immutable execution context.
    /// </summary>
    /// <param name="alias">The workflow alias.</param>
    /// <param name="arguments">The JSON argument object.</param>
    /// <param name="enabledToolGroups">The task tool-group whitelist.</param>
    /// <param name="executionContext">The immutable tool execution context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The compact JSON tool result.</returns>
    Task<string> InvokeAsync(
        string alias,
        JsonElement arguments,
        IEnumerable<string> enabledToolGroups,
        ToolExecutionContext executionContext,
        CancellationToken cancellationToken);
}
