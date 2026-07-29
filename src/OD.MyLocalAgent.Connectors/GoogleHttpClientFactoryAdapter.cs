using Google.Apis.Http;

namespace OD.MyLocalAgent.Connectors;

internal sealed class GoogleHttpClientFactoryAdapter : Google.Apis.Http.IHttpClientFactory
{
    private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;

    public GoogleHttpClientFactoryAdapter(System.Net.Http.IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var forwardingHandler = new FactoryBackedHttpMessageHandler(_httpClientFactory);
        var configurableHandler = new ConfigurableMessageHandler(forwardingHandler);
        return new ConfigurableHttpClient(configurableHandler);
    }

    private sealed class FactoryBackedHttpMessageHandler : HttpMessageHandler
    {
        private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;

        public FactoryBackedHttpMessageHandler(
            System.Net.Http.IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var client = _httpClientFactory.CreateClient(ConnectorHttpClientNames.Google);
            client.Timeout = Timeout.InfiniteTimeSpan;
            return await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
    }
}
