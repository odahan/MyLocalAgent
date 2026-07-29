namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    private readonly TimeSpan? _timeout;

    public StubHttpClientFactory(
        HttpMessageHandler handler,
        TimeSpan? timeout = null)
    {
        _handler = handler;
        _timeout = timeout;
    }

    public HttpClient CreateClient(string name)
    {
        var client = new HttpClient(_handler, disposeHandler: false);
        if (_timeout is not null)
        {
            client.Timeout = _timeout.Value;
        }

        return client;
    }
}
