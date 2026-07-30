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
        return new EffectiveGenerationPolicy(
            temperature.Value,
            topP.Value,
            defaults.ContextWindowTokens,
            temperature.Source,
            topP.Source);
    }

    /// <summary>
    /// Resolves a fixed internal policy with the current global context declaration.
    /// </summary>
    /// <param name="policy">The program-owned temperature and Top P values.</param>
    /// <param name="defaults">The validated global defaults supplying only context.</param>
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

        return new EffectiveGenerationPolicy(
            policy.Temperature,
            policy.TopP,
            defaults.ContextWindowTokens,
            GenerationOptionSource.Program,
            GenerationOptionSource.Program);
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
}
