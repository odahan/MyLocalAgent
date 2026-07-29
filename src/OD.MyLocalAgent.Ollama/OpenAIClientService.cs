using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Provides cloud language-model generation through an OpenAI-compatible endpoint.
/// </summary>
public sealed class OpenAIClientService : ILlmClientService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenAIOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenAIClientService> _logger;
    private readonly DynamicOpenAIChatClient _chatClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIClientService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The managed HTTP client factory.</param>
    /// <param name="options">The reloadable OpenAI configuration.</param>
    /// <param name="timeProvider">The time source used to measure latency.</param>
    /// <param name="logger">The structured logger.</param>
    public OpenAIClientService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenAIOptions> options,
        TimeProvider timeProvider,
        ILogger<OpenAIClientService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _chatClient = new DynamicOpenAIChatClient(httpClientFactory, options);
    }

    /// <inheritdoc />
    public IChatClient ChatClient => _chatClient;

    /// <inheritdoc />
    public string ProviderName => AIOptions.OpenAIProvider;

    /// <inheritdoc />
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var current = GetValidatedOptions();
        _logger.LogInformation(
            "Starting OpenAI generation with model {Model}.",
            current.Model);

        var response = await _chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions { ModelId = current.Model },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Completed OpenAI generation with model {Model}.",
            current.Model);
        return response.Text;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var current = GetValidatedOptions();
        _logger.LogInformation(
            "Starting streaming OpenAI generation with model {Model}.",
            current.Model);

        await foreach (var update in _chatClient
            .GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { ModelId = current.Model },
                cancellationToken)
            .ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }

        _logger.LogInformation(
            "Completed streaming OpenAI generation with model {Model}.",
            current.Model);
    }

    /// <inheritdoc />
    public async Task<LlmHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var start = _timeProvider.GetTimestamp();
        var current = GetValidatedOptions();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(current.TimeoutSeconds));

        try
        {
            var client = _httpClientFactory.CreateClient(LlmHttpClientNames.OpenAI);
            client.BaseAddress = LlmOptionsValidation.ParseEndpoint(
                current.Endpoint,
                OpenAIOptions.SectionName);
            client.Timeout = Timeout.InfiniteTimeSpan;

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"models/{Uri.EscapeDataString(current.Model)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.ApiKey);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);

            var latency = GetElapsedMilliseconds(start);
            var modelAvailable = response.IsSuccessStatusCode;
            var error = modelAvailable
                ? null
                : $"The OpenAI-compatible endpoint returned HTTP {(int)response.StatusCode} for model '{current.Model}'.";
            return new LlmHealthResult(true, modelAvailable, latency, error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            var latency = GetElapsedMilliseconds(start);
            _logger.LogWarning(
                exception,
                "OpenAI health check failed after {LatencyMs} ms.",
                latency);
            return new LlmHealthResult(false, false, latency, exception.Message);
        }
    }

    private OpenAIOptions GetValidatedOptions()
    {
        var current = _options.CurrentValue;
        var validation = LlmOptionsValidation.ValidateOpenAI(current, requireApiKey: true);
        return validation is null
            ? current
            : throw new LlmConfigurationException(validation);
    }

    private long GetElapsedMilliseconds(long start)
    {
        return (long)Math.Round(
            _timeProvider.GetElapsedTime(start).TotalMilliseconds,
            MidpointRounding.AwayFromZero);
    }
}
