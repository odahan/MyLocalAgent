using Google.Apis.Http;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Adapts the application's managed HTTP handler pipeline to the Google client library.
/// </summary>
internal sealed class GoogleHttpClientFactoryAdapter : Google.Apis.Http.IHttpClientFactory
{
    private readonly System.Net.Http.IHttpMessageHandlerFactory _httpMessageHandlerFactory;

    public GoogleHttpClientFactoryAdapter(
        System.Net.Http.IHttpMessageHandlerFactory httpMessageHandlerFactory)
    {
        _httpMessageHandlerFactory = httpMessageHandlerFactory;
    }

    public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // Reuse the named pipeline so Google requests receive the configured timeout, proxy, and diagnostics behavior.
        var managedHandler = _httpMessageHandlerFactory.CreateHandler(
            ConnectorHttpClientNames.Google);
        var configurableHandler = new ConfigurableMessageHandler(managedHandler);
        var configurableClient = new ConfigurableHttpClient(configurableHandler);

        foreach (var initializer in args.Initializers)
        {
            initializer.Initialize(configurableClient);
        }

        return configurableClient;
    }
}
