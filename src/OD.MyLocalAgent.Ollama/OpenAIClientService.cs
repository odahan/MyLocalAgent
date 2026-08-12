using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Provides cloud language-model generation through an OpenAI-compatible endpoint.
/// </summary>
public sealed class OpenAIClientService : ILlmClientService, IOpenAIModelCatalog
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenAIOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenAIClientService> _logger;
    private readonly DynamicOpenAIChatClient _chatClient;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIClientService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The managed HTTP client factory.</param>
    /// <param name="options">The reloadable OpenAI configuration.</param>
    /// <param name="timeProvider">The time source used to measure latency.</param>
    /// <param name="logger">The structured logger.</param>
    /// <param name="aiOptions">The optional reloadable global generation defaults.</param>
    public OpenAIClientService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenAIOptions> options,
        TimeProvider timeProvider,
        ILogger<OpenAIClientService> logger,
        IOptionsMonitor<AIOptions>? aiOptions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _aiOptions = aiOptions;
        _chatClient = new DynamicOpenAIChatClient(httpClientFactory, options);
    }

    /// <inheritdoc />
    public IChatClient ChatClient => _chatClient;

    /// <inheritdoc />
    public string ProviderName => AIOptions.OpenAIProvider;

    /// <inheritdoc />
    public string ModelName => _options.CurrentValue.Model;

    /// <inheritdoc />
    public Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken) =>
        GenerateAsync(prompt, ResolveGlobalPolicy(), cancellationToken);

    /// <inheritdoc />
    public async Task<string> GenerateAsync(
        string prompt,
        EffectiveGenerationPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(policy);
        var current = GetValidatedOptions();
        _logger.LogInformation(
            "Starting OpenAI generation with model {Model}.",
            current.Model);

        var policyClient = new GenerationPolicyChatClient(
            _chatClient,
            policy,
            ProviderName);
        var response = await policyClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions { ModelId = current.Model },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Completed OpenAI generation with model {Model}.",
            current.Model);
        return response.Text;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        CancellationToken cancellationToken) =>
        GenerateStreamingAsync(
            prompt,
            ResolveGlobalPolicy(),
            cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        EffectiveGenerationPolicy policy,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(policy);
        var current = GetValidatedOptions();
        _logger.LogInformation(
            "Starting streaming OpenAI generation with model {Model}.",
            current.Model);

        var policyClient = new GenerationPolicyChatClient(
            _chatClient,
            policy,
            ProviderName);
        await foreach (var update in policyClient
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

            using var request = new HttpRequestMessage(HttpMethod.Get, "models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.ApiKey);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);

            var latency = GetElapsedMilliseconds(start);
            var models = response.IsSuccessStatusCode
                ? await ReadModelCatalogAsync(response, timeoutSource.Token).ConfigureAwait(false)
                : [];
            var modelAvailable = models.Contains(current.Model, StringComparer.Ordinal);
            var error = modelAvailable
                ? null
                : response.IsSuccessStatusCode
                    ? $"The OpenAI-compatible endpoint did not list model '{current.Model}'."
                    : $"The OpenAI-compatible endpoint returned HTTP {(int)response.StatusCode} while listing models.";
            return new LlmHealthResult(true, modelAvailable, latency, error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListModelsAsync(
        string endpoint,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var current = _options.CurrentValue with
        {
            Endpoint = endpoint,
            TimeoutSeconds = timeoutSeconds,
        };
        var validation = LlmOptionsValidation.ValidateOpenAI(current, requireApiKey: true);
        if (validation is not null)
        {
            throw new LlmConfigurationException(validation);
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var client = _httpClientFactory.CreateClient(LlmHttpClientNames.OpenAI);
        client.BaseAddress = LlmOptionsValidation.ParseEndpoint(
            current.Endpoint,
            OpenAIOptions.SectionName);
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var request = new HttpRequestMessage(HttpMethod.Get, "models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", current.ApiKey);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadModelCatalogAsync(response, timeoutSource.Token).ConfigureAwait(false);
    }

    private long GetElapsedMilliseconds(long start)
    {
        return (long)Math.Round(
            _timeProvider.GetElapsedTime(start).TotalMilliseconds,
            MidpointRounding.AwayFromZero);
    }

    private static async Task<IReadOnlyList<string>> ReadModelCatalogAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var contentStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument
            .ParseAsync(contentStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The model catalog response did not contain a data array.");
        }

        return data
            .EnumerateArray()
            .Select(
                static entry => entry.TryGetProperty("id", out var id)
                    ? id.GetString()
                    : null)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private EffectiveGenerationPolicy ResolveGlobalPolicy() =>
        GenerationPolicyResolver.Resolve(
            _aiOptions?.CurrentValue.GenerationDefaults ??
            new AIGenerationDefaults());
}
