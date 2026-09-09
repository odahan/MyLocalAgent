using System.Text.Json;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Builds one deterministic object-form workflow step from Tool Map metadata.
/// </summary>
/// <remarks>
/// This class is pure and free of user-interface dependencies. It is the single owner of the
/// unique-id, required-argument, and AI-default algorithms shared by the Add step command and
/// the Tool catalog dialog.
/// </remarks>
public static class WorkflowSnippetBuilder
{
    /// <summary>
    /// Gets the default prompt emitted for a new <c>ai.prompt</c> step.
    /// </summary>
    public const string DefaultPromptText = "Use the workflow context to complete this step.";

    private const int DefaultRetryCount = 1;
    private const int DefaultRetryBackoffSeconds = 2;

    /// <summary>
    /// Creates a unique effective identifier for an alias.
    /// </summary>
    /// <param name="alias">The Tool Map alias.</param>
    /// <param name="workflow">The current workflow steps.</param>
    /// <returns>An identifier that is unique case-insensitively among all effective ids.</returns>
    public static string CreateUniqueId(
        string alias,
        IReadOnlyList<WorkflowStepDefinition> workflow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(workflow);

        var used = new HashSet<string>(
            workflow.Select(static step => step.EffectiveId),
            StringComparer.OrdinalIgnoreCase);
        var baseId = alias.Replace('.', '-');
        var candidate = baseId;
        var suffix = 2;
        while (used.Contains(candidate))
        {
            candidate = $"{baseId}-{suffix++}";
        }

        return candidate;
    }

    /// <summary>
    /// Creates one object-form step for the supplied Tool Map entry.
    /// </summary>
    /// <param name="tool">The Tool Map entry to instantiate.</param>
    /// <param name="options">The selected insertion options.</param>
    /// <param name="workflow">The current workflow steps used for id uniqueness.</param>
    /// <param name="errorPolicy">The current task error policy.</param>
    /// <param name="temperature">The effective task Temperature.</param>
    /// <param name="topP">The effective task Top P.</param>
    /// <returns>The deterministic step definition.</returns>
    public static WorkflowStepDefinition Create(
        ToolDefinition tool,
        WorkflowSnippetOptions options,
        IReadOnlyList<WorkflowStepDefinition> workflow,
        WorkflowErrorPolicy errorPolicy,
        double temperature,
        double topP)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(workflow);

        var isPrompt = string.Equals(
            tool.Alias,
            ToolMap.PromptAlias,
            StringComparison.OrdinalIgnoreCase);
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var argument in tool.ArgumentDefinitions)
        {
            var isContextArgument = isPrompt && string.Equals(
                argument.Name,
                "includeContext",
                StringComparison.OrdinalIgnoreCase);
            if (isContextArgument)
            {
                arguments[argument.Name] = options.IncludeAccumulatedContext;
                continue;
            }

            if (!argument.IsRequired && !options.IncludeOptionalArguments)
            {
                continue;
            }

            arguments[argument.Name] = PlaceholderValue(tool, argument);
        }

        return new WorkflowStepDefinition
        {
            Step = tool.Alias,
            Id = CreateUniqueId(tool.Alias, workflow),
            Args = JsonSerializer.SerializeToElement(arguments),
            OnError = options.IncludeErrorPolicy ? errorPolicy : null,
            RetryCount = options.IncludeErrorPolicy ? DefaultRetryCount : null,
            RetryBackoffSeconds = options.IncludeErrorPolicy ? DefaultRetryBackoffSeconds : null,
            GenerationOptions = options.IncludeGenerationOptions && tool.IsAiStep
                ? new GenerationOptions { Temperature = temperature, TopP = topP }
                : null,
        };
    }

    private static object? PlaceholderValue(
        ToolDefinition tool,
        ToolArgumentDefinition argument)
    {
        if (string.Equals(tool.Alias, ToolMap.PromptAlias, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(argument.Name, "prompt", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultPromptText;
        }

        return argument.Kind switch
        {
            WorkflowArgumentKind.Integer or WorkflowArgumentKind.Number => 0,
            WorkflowArgumentKind.Boolean or WorkflowArgumentKind.BooleanOrString => false,
            WorkflowArgumentKind.Object => new Dictionary<string, object?>(),
            WorkflowArgumentKind.Enumeration => argument.AllowedValues.Count > 0
                ? argument.AllowedValues[0]
                : string.Empty,
            _ => string.Empty,
        };
    }
}
