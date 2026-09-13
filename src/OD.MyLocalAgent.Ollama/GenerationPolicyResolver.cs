namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Resolves immutable call policies by merging step, task, and Settings values per property.
/// </summary>
public static class GenerationPolicyResolver
{
    /// <summary>
    /// Resolves a normal user-directed generation policy.
    /// </summary>
    /// <param name="defaults">The validated global defaults.</param>
    /// <param name="taskOptions">The optional task overrides.</param>
    /// <param name="stepOptions">The optional AI-step overrides.</param>
    /// <returns>The fully resolved policy and independent provenance.</returns>
    public static EffectiveGenerationPolicy Resolve(
        AIGenerationDefaults defaults,
        GenerationOptions? taskOptions = null,
        GenerationOptions? stepOptions = null)
    {
        Validate(defaults, taskOptions, stepOptions);
        var temperature = ResolveValue(
            stepOptions?.Temperature,
            taskOptions?.Temperature,
            defaults.Temperature);
        var topP = ResolveValue(
            stepOptions?.TopP,
            taskOptions?.TopP,
            defaults.TopP);
        var reasoning = ResolveReasoning(defaults, taskOptions, stepOptions);
        return new EffectiveGenerationPolicy(
            temperature.Value,
            topP.Value,
            defaults.ContextWindowTokens,
            temperature.Source,
            topP.Source)
        {
            ReasoningEnabled = reasoning.Enabled,
            ReasoningEffort = reasoning.Effort,
        };
    }

    /// <summary>
    /// Resolves a fixed internal policy with the current global context declaration.
    /// </summary>
    /// <param name="policy">The program-owned temperature and Top P values.</param>
    /// <param name="defaults">The validated global defaults and internal reasoning preferences.</param>
    /// <returns>The fully resolved internal policy.</returns>
    public static EffectiveGenerationPolicy ResolveInternal(
        InternalGenerationPolicy policy,
        AIGenerationDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var defaultsError = GenerationPolicyValidation.ValidateDefaults(defaults);
        if (defaultsError is not null)
        {
            throw new LlmConfigurationException(
                $"AI:GenerationDefaults is invalid. {defaultsError}");
        }

        var thinking = defaults.InternalThinking.Get(policy.ThinkingCategory);
        var reasoning = thinking.Mode switch
        {
            InternalThinkingMode.Disabled => new ResolvedReasoning(false, null, false),
            InternalThinkingMode.ProviderDefault => new ResolvedReasoning(false, null, true),
            InternalThinkingMode.SpecificLevel => new ResolvedReasoning(true, thinking.Effort, false),
            _ => throw new LlmConfigurationException("Internal thinking mode is invalid."),
        };
        return new EffectiveGenerationPolicy(
            policy.Temperature,
            policy.TopP,
            defaults.ContextWindowTokens,
            GenerationOptionSource.Program,
            GenerationOptionSource.Program)
        {
            ReasoningEnabled = reasoning.Enabled,
            ReasoningEffort = reasoning.Effort,
            UseProviderReasoningDefault = reasoning.UseProviderDefault,
        };
    }

    private static void Validate(
        AIGenerationDefaults defaults,
        GenerationOptions? taskOptions,
        GenerationOptions? stepOptions)
    {
        var defaultsError = GenerationPolicyValidation.ValidateDefaults(defaults);
        if (defaultsError is not null)
        {
            throw new LlmConfigurationException(
                $"AI:GenerationDefaults is invalid. {defaultsError}");
        }

        if (taskOptions is not null &&
            GenerationPolicyValidation.ValidateOverrides(taskOptions) is { } taskError)
        {
            throw new LlmConfigurationException(
                $"Task generation options are invalid. {taskError}");
        }

        if (stepOptions is not null &&
            GenerationPolicyValidation.ValidateOverrides(stepOptions) is { } stepError)
        {
            throw new LlmConfigurationException(
                $"Workflow-step generation options are invalid. {stepError}");
        }
    }

    private static ResolvedValue ResolveValue(
        double? step,
        double? task,
        double settings) =>
        step is { } stepValue
            ? new ResolvedValue(stepValue, GenerationOptionSource.Step)
            : task is { } taskValue
                ? new ResolvedValue(taskValue, GenerationOptionSource.Task)
                : new ResolvedValue(settings, GenerationOptionSource.Settings);

    private readonly record struct ResolvedValue(
        double Value,
        GenerationOptionSource Source);

    private static ResolvedReasoning ResolveReasoning(
        AIGenerationDefaults defaults,
        GenerationOptions? taskOptions,
        GenerationOptions? stepOptions)
    {
        if (stepOptions?.ReasoningEffort is { } stepEffort)
        {
            return new ResolvedReasoning(true, stepEffort, false);
        }

        if (stepOptions?.ReasoningEnabled is { } stepEnabled)
        {
            return new ResolvedReasoning(stepEnabled, null, false);
        }

        if (taskOptions?.ReasoningEffort is { } taskEffort)
        {
            return new ResolvedReasoning(true, taskEffort, false);
        }

        if (taskOptions?.ReasoningEnabled is { } taskEnabled)
        {
            return new ResolvedReasoning(taskEnabled, null, false);
        }

        return defaults.ReasoningEffort is { } defaultEffort
            ? new ResolvedReasoning(true, defaultEffort, false)
            : new ResolvedReasoning(defaults.ReasoningEnabled, null, false);
    }

    private readonly record struct ResolvedReasoning(
        bool Enabled,
        string? Effort,
        bool UseProviderDefault);
}
