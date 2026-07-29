using Microsoft.Extensions.DependencyInjection;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Registers workflow execution and report-writing services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds workflow services to the shared application composition.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddWorkflowServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IWorkflowEngine, SequentialWorkflowEngine>();
        services.AddSingleton<IReportWriter, MarkdownReportWriter>();
        return services;
    }
}
