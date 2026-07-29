using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;

namespace OD.MyLocalAgent.Ollama;

internal sealed class DynamicOpenAIChatClient : IChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenAIOptions> _options;

    public DynamicOpenAIChatClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenAIOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var current = GetValidatedOptions();
        using var timeoutSource = CreateTimeoutSource(current.TimeoutSeconds, cancellationToken);
        options ??= new ChatOptions();
        options.ModelId = current.Model;
        using var client = CreateClient(current);
        return await client.GetResponseAsync(messages, options, timeoutSource.Token).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var current = GetValidatedOptions();
        using var timeoutSource = CreateTimeoutSource(current.TimeoutSeconds, cancellationToken);
        options ??= new ChatOptions();
        options.ModelId = current.Model;
        using var client = CreateClient(current);

        await foreach (var update in client
            .GetStreamingResponseAsync(messages, options, timeoutSource.Token)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private IChatClient CreateClient(OpenAIOptions options)
    {
        var httpClient = _httpClientFactory.CreateClient(LlmHttpClientNames.OpenAI);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = LlmOptionsValidation.ParseEndpoint(
                options.Endpoint,
                OpenAIOptions.SectionName),
            Transport = new HttpClientPipelineTransport(httpClient),
        };
        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey!),
            clientOptions);
        return openAIClient.GetChatClient(options.Model).AsIChatClient();
    }

    private OpenAIOptions GetValidatedOptions()
    {
        var current = _options.CurrentValue;
        var validation = LlmOptionsValidation.ValidateOpenAI(current, requireApiKey: true);
        return validation is null
            ? current
            : throw new LlmConfigurationException(validation);
    }

    private static CancellationTokenSource CreateTimeoutSource(
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return source;
    }
}
