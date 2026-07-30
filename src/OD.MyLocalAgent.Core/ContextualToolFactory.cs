using Microsoft.Extensions.DependencyInjection;

namespace OD.MyLocalAgent.Core;

internal sealed class ContextualToolFactory<TTool> : IContextualToolFactory<TTool>
    where TTool : class
{
    private readonly IServiceProvider _services;

    public ContextualToolFactory(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public TTool Create(ToolExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ActivatorUtilities.CreateInstance<TTool>(_services, context);
    }
}
