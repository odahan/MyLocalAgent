namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Validates global defaults and nullable generation overrides.
/// </summary>
public static class GenerationPolicyValidation
{
    /// <summary>
    /// Gets the minimum supported context-window declaration.
    /// </summary>
    public const int MinimumContextWindowTokens = 1024;

    /// <summary>
    /// Gets the maximum supported context-window declaration.
    /// </summary>
    public const int MaximumContextWindowTokens = 2_000_000;

    /// <summary>
    /// Validates global generation defaults.
    /// </summary>
    /// <param name="defaults">The values to validate.</param>
    /// <returns>An English validation error, or <see langword="null"/>.</returns>
    public static string? ValidateDefaults(AIGenerationDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        var valuesError = ValidateValues(
            defaults.Temperature,
            defaults.TopP,
            defaults.ContextWindowTokens,
            defaults.ReasoningEnabled,
            defaults.ReasoningEffort);
        return valuesError ?? ValidateInternalThinking(defaults.InternalThinking);
    }

    /// <summary>
    /// Validates optional generation overrides.
    /// </summary>
    /// <param name="options">The override values to validate.</param>
    /// <returns>An English validation error, or <see langword="null"/>.</returns>
    public static string? ValidateOverrides(GenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Temperature is { } temperature &&
            (!double.IsFinite(temperature) || temperature is < 0 or > 2))
        {
            return "Temperature must be a finite number from 0 through 2.";
        }

        if (options.TopP is { } topP &&
            (!double.IsFinite(topP) || topP is <= 0 or > 1))
        {
            return "Top P must be a finite number greater than 0 and at most 1.";
        }

        if (options.ReasoningEffort is { } reasoningEffort)
        {
            if (ValidateReasoningEffort(reasoningEffort) is { } effortError)
            {
                return effortError;
            }

            if (options.ReasoningEnabled == false)
            {
                return "Reasoning effort cannot be set when reasoning is disabled.";
            }
        }

        return null;
    }

    /// <summary>Validates one open-ended provider reasoning-effort keyword.</summary>
    /// <param name="reasoningEffort">The exact keyword.</param>
    /// <returns>An English validation error, or <see langword="null"/>.</returns>
    public static string? ValidateReasoningEffort(string? reasoningEffort)
    {
        if (string.IsNullOrWhiteSpace(reasoningEffort))
        {
            return "Reasoning effort must not be empty or whitespace.";
        }

        if (!string.Equals(reasoningEffort, reasoningEffort.Trim(), StringComparison.Ordinal))
        {
            return "Reasoning effort must not start or end with whitespace.";
        }

        return reasoningEffort.Length > 128
            ? "Reasoning effort must not exceed 128 characters."
            : null;
    }

    /// <summary>Validates independently configured internal thinking preferences.</summary>
    public static string? ValidateInternalThinking(InternalThinkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var category in Enum.GetValues<InternalThinkingCategory>())
        {
            var setting = options.Get(category);
            if (!Enum.IsDefined(setting.Mode))
            {
                return $"Internal thinking mode for '{category}' is invalid.";
            }

            if (setting.Mode == InternalThinkingMode.SpecificLevel)
            {
                if (ValidateReasoningEffort(setting.Effort) is { } error)
                {
                    return $"Internal thinking level for '{category}' is invalid. {error}";
                }
            }
            else if (setting.Effort is not null)
            {
                return $"Internal thinking level for '{category}' is valid only with SpecificLevel.";
            }
        }

        return null;
    }

    private static string? ValidateValues(
        double temperature,
        double topP,
        int contextWindowTokens,
        bool reasoningEnabled,
        string? reasoningEffort)
    {
        var overrideError = ValidateOverrides(
            new GenerationOptions
            {
                Temperature = temperature,
                TopP = topP,
                ReasoningEnabled = reasoningEnabled,
                ReasoningEffort = reasoningEffort,
            });
        if (overrideError is not null)
        {
            return overrideError;
        }

        return contextWindowTokens is
            < MinimumContextWindowTokens or > MaximumContextWindowTokens
            ? $"Context window must be from {MinimumContextWindowTokens:N0} through {MaximumContextWindowTokens:N0} tokens."
            : null;
    }
}
