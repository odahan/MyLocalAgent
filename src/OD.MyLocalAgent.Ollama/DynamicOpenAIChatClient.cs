using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using System.Runtime.CompilerServices;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Creates short-lived OpenAI clients from the current options so settings changes take effect without restart.
/// </summary>
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
        var timeoutSeconds = TimeoutExecutionScope.TimeoutSeconds ?? current.TimeoutSeconds;
        using var timeoutSource = CreateTimeoutSource(timeoutSeconds, cancellationToken);
        var effectiveOptions = options?.Clone() ?? new ChatOptions();
        var model = options?.ModelId ?? current.Model;
        effectiveOptions.ModelId = model;
        using var client = CreateClient(current, model, timeoutSeconds);
        return await client.GetResponseAsync(messages, effectiveOptions, timeoutSource.Token).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var current = GetValidatedOptions();
        var timeoutSeconds = TimeoutExecutionScope.TimeoutSeconds ?? current.TimeoutSeconds;
        using var timeoutSource = CreateTimeoutSource(timeoutSeconds, cancellationToken);
        var effectiveOptions = options?.Clone() ?? new ChatOptions();
        var model = options?.ModelId ?? current.Model;
        effectiveOptions.ModelId = model;
        using var client = CreateClient(current, model, timeoutSeconds);

        ChatFinishReason? finishReason = null;
        var updateCount = 0;
        var textCharacterCount = 0;
        await foreach (var update in client
            .GetStreamingResponseAsync(messages, effectiveOptions, timeoutSource.Token)
            .ConfigureAwait(false))
        {
            updateCount++;
            textCharacterCount += update.Text?.Length ?? 0;
            finishReason = update.FinishReason ?? finishReason;
            yield return update;
        }

        OpenAIStreamingCompletionValidator.EnsureCompleted(
            finishReason,
            updateCount,
            textCharacterCount);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private IChatClient CreateClient(
        OpenAIOptions options,
        string model,
        int timeoutSeconds)
    {
        var httpClient = _httpClientFactory.CreateClient(LlmHttpClientNames.OpenAI);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = LlmOptionsValidation.ParseEndpoint(
                options.Endpoint,
                OpenAIOptions.SectionName),
            NetworkTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            Transport = new HttpClientPipelineTransport(httpClient),
        };
        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey!),
            clientOptions);
        return openAIClient.GetChatClient(model).AsIChatClient();
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
