namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public StubHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(_handler, disposeHandler: false);
    }
}
