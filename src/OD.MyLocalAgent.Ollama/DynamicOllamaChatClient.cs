using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using System.Runtime.CompilerServices;

namespace OD.MyLocalAgent.Ollama;

internal sealed class DynamicOllamaChatClient : IChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OllamaOptions> _options;
    private readonly Func<CancellationToken, Task> _ensureModelAvailableAsync;

    public DynamicOllamaChatClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OllamaOptions> options,
        Func<CancellationToken, Task> ensureModelAvailableAsync)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _ensureModelAvailableAsync = ensureModelAvailableAsync;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var current = _options.CurrentValue;
        using var timeoutSource = CreateTimeoutSource(current.TimeoutSeconds, cancellationToken);

        try
        {
            await _ensureModelAvailableAsync(timeoutSource.Token).ConfigureAwait(false);
            options ??= new ChatOptions();
            options.ModelId = current.Model;
            using var client = CreateClient(current);
            return await client.GetResponseAsync(messages, options, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaUnavailableException(
                $"Ollama did not respond within {current.TimeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaUnavailableException(
                $"Ollama is unavailable at '{current.Endpoint}'.",
                exception);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var current = _options.CurrentValue;
        using var timeoutSource = CreateTimeoutSource(current.TimeoutSeconds, cancellationToken);

        IChatClient? client = null;
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
        try
        {
            try
            {
                await _ensureModelAvailableAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new OllamaUnavailableException(
                    $"Ollama did not respond within {current.TimeoutSeconds} seconds.",
                    exception);
            }
            catch (HttpRequestException exception)
            {
                throw new OllamaUnavailableException(
                    $"Ollama is unavailable at '{current.Endpoint}'.",
                    exception);
            }

            options ??= new ChatOptions();
            options.ModelId = current.Model;
            client = CreateClient(current);
            enumerator = client
                .GetStreamingResponseAsync(messages, options, timeoutSource.Token)
                .GetAsyncEnumerator(timeoutSource.Token);

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new OllamaUnavailableException(
                        $"Ollama did not respond within {current.TimeoutSeconds} seconds.",
                        exception);
                }
                catch (HttpRequestException exception)
                {
                    throw new OllamaUnavailableException(
                        $"Ollama is unavailable at '{current.Endpoint}'.",
                        exception);
                }

                if (!hasNext)
                {
                    break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            client?.Dispose();
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

    private IChatClient CreateClient(OllamaOptions options)
    {
        var httpClient = _httpClientFactory.CreateClient(LlmHttpClientNames.Ollama);
        httpClient.BaseAddress = LlmOptionsValidation.ParseEndpoint(options.Endpoint, OllamaOptions.SectionName);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        return new OllamaApiClient(httpClient, options.Model);
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
