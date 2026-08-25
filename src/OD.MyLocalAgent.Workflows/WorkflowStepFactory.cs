using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Converts normalized Core definitions into executable workflow step types.
/// </summary>
public static class WorkflowStepFactory
{
    /// <summary>
    /// Creates an executable step using Core's single Tool Map registry.
    /// </summary>
    /// <param name="definition">The normalized persisted definition.</param>
    /// <param name="toolRegistry">The Core Tool Map registry.</param>
    /// <returns>The executable step.</returns>
    public static WorkflowStep Create(
        WorkflowStepDefinition definition,
        IToolRegistry toolRegistry)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(toolRegistry);

        if (!toolRegistry.TryGetDefinition(definition.Step, out var toolDefinition) ||
            toolDefinition is null)
        {
            throw new WorkflowValidationException(
                $"Unknown Tool Map alias '{definition.Step}'.");
        }

        if (definition.ForEach is not null && !toolDefinition.SupportsForEach)
        {
            throw new WorkflowValidationException(
                $"Workflow alias '{definition.Step}' does not support 'forEach'.");
        }

        if (!toolDefinition.IsAiStep)
        {
            if (toolDefinition.IsControlFlowStep)
            {
                return new DecisionStep(definition);
            }

            return new ToolStep(definition);
        }

        if (string.Equals(
                toolDefinition.Alias,
                ToolMap.PromptAlias,
                StringComparison.OrdinalIgnoreCase))
        {
            return new PromptStep(definition);
        }

        const string aiPrefix = "ai.";
        if (!toolDefinition.Alias.StartsWith(
                aiPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkflowValidationException(
                $"AI step alias '{toolDefinition.Alias}' is invalid.");
        }

        return new SkillStep(
            definition,
            toolDefinition.Alias[aiPrefix.Length..]);
    }
}
