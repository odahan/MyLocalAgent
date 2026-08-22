using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OllamaSharp;
using System.Runtime.CompilerServices;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Creates short-lived Ollama clients from the current options and verifies the selected model before use.
/// </summary>
internal sealed class DynamicOllamaChatClient : IChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OllamaOptions> _options;
    private readonly Func<string, CancellationToken, Task> _ensureModelAvailableAsync;

    public DynamicOllamaChatClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OllamaOptions> options,
        Func<string, CancellationToken, Task> ensureModelAvailableAsync)
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
        var model = options?.ModelId ?? current.Model;
        var timeoutSeconds = TimeoutExecutionScope.TimeoutSeconds ?? current.TimeoutSeconds;
        using var timeoutSource = CreateTimeoutSource(timeoutSeconds, cancellationToken);

        try
        {
            await _ensureModelAvailableAsync(model, timeoutSource.Token).ConfigureAwait(false);
            var effectiveOptions = options?.Clone() ?? new ChatOptions();
            effectiveOptions.ModelId = model;
            using var client = CreateClient(current, model);
            return await client.GetResponseAsync(messages, effectiveOptions, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OllamaUnavailableException(
                $"The configured model '{model}' did not respond within {timeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaUnavailableException(
                $"The configured model '{model}' is unavailable at '{current.Endpoint}'.",
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
        var model = options?.ModelId ?? current.Model;
        var timeoutSeconds = TimeoutExecutionScope.TimeoutSeconds ?? current.TimeoutSeconds;
        using var timeoutSource = CreateTimeoutSource(timeoutSeconds, cancellationToken);

        IChatClient? client = null;
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
        try
        {
            try
            {
                await _ensureModelAvailableAsync(model, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new OllamaUnavailableException(
                    $"The configured model '{model}' did not respond within {timeoutSeconds} seconds.",
                    exception);
            }
            catch (HttpRequestException exception)
            {
                throw new OllamaUnavailableException(
                    $"The configured model '{model}' is unavailable at '{current.Endpoint}'.",
                    exception);
            }

            var effectiveOptions = options?.Clone() ?? new ChatOptions();
            effectiveOptions.ModelId = model;
            client = CreateClient(current, model);
            enumerator = client
                .GetStreamingResponseAsync(messages, effectiveOptions, timeoutSource.Token)
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
                        $"The configured model '{model}' did not respond within {timeoutSeconds} seconds.",
                        exception);
                }
                catch (HttpRequestException exception)
                {
                    throw new OllamaUnavailableException(
                        $"The configured model '{model}' is unavailable at '{current.Endpoint}'.",
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

    private IChatClient CreateClient(OllamaOptions options, string model)
    {
        var httpClient = _httpClientFactory.CreateClient(LlmHttpClientNames.Ollama);
        httpClient.BaseAddress = LlmOptionsValidation.ParseEndpoint(options.Endpoint, OllamaOptions.SectionName);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        return new OllamaApiClient(httpClient, model);
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
