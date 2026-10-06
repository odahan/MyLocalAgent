using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Adds language-model provider services to dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers reloadable provider options, managed HTTP clients, providers, and the provider factory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddLlmProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<AIOptions>()
            .Bind(configuration.GetSection(AIOptions.SectionName))
            .Validate(
                static options => LlmOptionsValidation.ValidateAI(options) is null,
                "AI provider or generation-default configuration is invalid.");
        services
            .AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(
                static options => LlmOptionsValidation.ValidateOllama(options) is null,
                "Ollama configuration is invalid.");
        services
            .AddOptions<OpenAIOptions>()
            .Bind(configuration.GetSection(OpenAIOptions.SectionName))
            .Validate(
                static options => LlmOptionsValidation.ValidateOpenAI(options, requireApiKey: false) is null,
                "OpenAI configuration is invalid.");
        services
            .AddOptions<LlmExecutionOptions>()
            .Bind(configuration.GetSection(LlmExecutionOptions.SectionName))
            .Validate(
                static options => LlmOptionsValidation.ValidateExecution(options) is null,
                "LLM execution concurrency configuration is invalid.");

        services.AddLogging();
        services.AddHttpClient(LlmHttpClientNames.Ollama);
        services.AddHttpClient(LlmHttpClientNames.OpenAI);
        services.AddHttpClient(OllamaLocalDecisionClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        services.AddSingleton<DecisionModelResidencyCoordinator>();
        services.AddSingleton<GlobalLlmExecutionGate>();
        services.AddSingleton<ILocalDecisionClient, OllamaLocalDecisionClient>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<OllamaClientService>();
        services.AddSingleton<ILlmModelWarmup>(
            static services => services.GetRequiredService<OllamaClientService>());
        services.AddSingleton<IOllamaModelCatalog>(
            static services => services.GetRequiredService<OllamaClientService>());
        services.AddSingleton<OpenAIClientService>();
        services.AddSingleton<IOpenAIModelCatalog>(
            static services => services.GetRequiredService<OpenAIClientService>());
        services.AddSingleton<IReasoningEffortValidator, ReasoningEffortValidator>();
        services.AddSingleton<ILlmClientFactory, LlmClientFactory>();
        return services;
    }
}
