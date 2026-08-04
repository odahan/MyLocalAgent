using Microsoft.Extensions.DependencyInjection;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Activates tool targets using ServiceProvider and ActivatorUtilities.
/// </summary>
public sealed class ToolTargetActivator : IToolTargetActivator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IToolCatalog _catalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolTargetActivator"/> class.
    /// </summary>
    /// <param name="serviceProvider">The root service provider.</param>
    /// <param name="catalog">The tool catalog snapshot.</param>
    public ToolTargetActivator(IServiceProvider serviceProvider, IToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(catalog);
        _serviceProvider = serviceProvider;
        _catalog = catalog;
    }

    /// <inheritdoc />
    public object CreateTarget(Type toolType, ToolExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(toolType);
        ArgumentNullException.ThrowIfNull(context);

        var definition = _catalog.Definitions.FirstOrDefault(d => d.DeclaringType == toolType)
            ?? throw new InvalidOperationException($"Type '{toolType.FullName}' is not a registered tool in the catalog.");

        if (definition.Activation == ToolActivationKind.Singleton)
        {
            return _serviceProvider.GetRequiredService(toolType);
        }

        return ActivatorUtilities.CreateInstance(_serviceProvider, toolType, context);
    }
}
