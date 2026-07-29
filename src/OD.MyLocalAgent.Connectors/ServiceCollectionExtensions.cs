using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            .Bind(configuration.GetSection(DropboxOptions.SectionName))
            .Validate(
                static options => ConnectorOptionsValidation.ValidateDropbox(options) is null,
                "Dropbox credential configuration is invalid.");

        services.AddHttpClient(ConnectorHttpClientNames.Google);
        services.AddHttpClient(ConnectorHttpClientNames.Dropbox);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IGoogleCredentialProvider, GoogleCredentialProvider>();
        services.AddSingleton<IGmailConnector, GmailConnector>();
        services.AddSingleton<ICalendarConnector, CalendarConnector>();
        services.AddSingleton<IDropboxConnector, DropboxConnector>();
        return services;
    }
}
