using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Registers prompt-injection guardrail services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers monitored options and the singleton content-trust service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The layered application configuration.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddGuardrails(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<GuardrailsOptions>()
            .Bind(configuration.GetSection(GuardrailsOptions.SectionName))
            .Validate(
                static options =>
                    options.CautionThreshold >= 0 &&
                    options.CautionThreshold < options.CleanThreshold &&
                    options.CleanThreshold <= 100 &&
                    options.MaxContentChars > 0 &&
                    options.MaxSummaryChars >= 256 &&
                    options.MaxSummaryChars <= options.MaxContentChars &&
                    options.MaxSummarizationSourceChars > options.MaxContentChars &&
                    options.TimeoutSeconds > 0 &&
                    options.MaxGmailMessagesPerOperation >= 0,
                "Guardrails thresholds, content limits, timeout, or Gmail limit are invalid.");
        services.AddSingleton<IContentTrustService, ContentTrustService>();
        return services;
    }
}
