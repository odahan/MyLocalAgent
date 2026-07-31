namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubHttpMessageHandlerFactory : IHttpMessageHandlerFactory
{
    private readonly HttpMessageHandler _handler;

    public StubHttpMessageHandlerFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpMessageHandler CreateHandler(string name)
    {
        return _handler;
    }
}
