using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class DelegateContextualToolFactory<TTool> :
    IContextualToolFactory<TTool>
{
    private readonly Func<ToolExecutionContext, TTool> _factory;

    public DelegateContextualToolFactory(
        Func<ToolExecutionContext, TTool> factory)
    {
        _factory = factory;
    }

    public TTool Create(ToolExecutionContext context) => _factory(context);
}
