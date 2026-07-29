using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Adds external connector services to dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers connector options, managed HTTP clients, credential support, and connector implementations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddConnectors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<GoogleOptions>()
            .Bind(configuration.GetSection(GoogleOptions.SectionName))
            .Validate(
                static options => ConnectorOptionsValidation.ValidateGoogle(options) is null,
                "Google OAuth configuration is invalid.");
        services
            .AddOptions<DropboxOptions>()
            .Bind(configuration.GetSection(DropboxOptions.SectionName));
        services
            .AddOptions<WebSearchOptions>()
            .Bind(configuration.GetSection(WebSearchOptions.SectionName))
            .Validate(
                static options =>
                    ConnectorOptionsValidation.ValidateWebSearch(options) is null,
                "Web search configuration is invalid.");

        services.AddHttpClient(ConnectorHttpClientNames.Google);
        services.AddHttpClient(ConnectorHttpClientNames.Dropbox);
        services.AddHttpClient(
            ConnectorHttpClientNames.WebSearch,
            static (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptionsMonitor<WebSearchOptions>>()
                    .CurrentValue;
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IGoogleCredentialProvider, GoogleCredentialProvider>();
        services.AddSingleton<IGmailConnector, GmailConnector>();
        services.AddSingleton<ICalendarConnector, CalendarConnector>();
        services.AddSingleton<IDropboxConnector, DropboxConnector>();
        services.AddSingleton<IWebSearchProvider, OllamaWebSearchConnector>();
        services.AddSingleton<IWebSearchConnector, WebSearchConnector>();
        return services;
    }
}
