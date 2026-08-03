using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;
using System.Runtime.CompilerServices;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Provides local language-model generation through Ollama.
/// </summary>
public sealed class OllamaClientService : ILlmClientService, IOllamaModelCatalog
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OllamaOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OllamaClientService> _logger;
    private readonly DynamicOllamaChatClient _chatClient;
    private readonly IOptionsMonitor<AIOptions>? _aiOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaClientService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The managed HTTP client factory.</param>
    /// <param name="options">The reloadable Ollama configuration.</param>
    /// <param name="timeProvider">The time source used to measure latency.</param>
    /// <param name="logger">The structured logger.</param>
    /// <param name="aiOptions">The optional reloadable global generation defaults.</param>
    public OllamaClientService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OllamaOptions> options,
        TimeProvider timeProvider,
        ILogger<OllamaClientService> logger,
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
        _chatClient = new DynamicOllamaChatClient(
            httpClientFactory,
            options,
            EnsureModelAvailableAsync);
    }

    /// <inheritdoc />
    public IChatClient ChatClient => _chatClient;

    /// <inheritdoc />
    public string ProviderName => AIOptions.OllamaProvider;

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
        ValidatePrompt(prompt);
        ArgumentNullException.ThrowIfNull(policy);
        var current = _options.CurrentValue;
        _logger.LogInformation(
            "Starting Ollama generation with model {Model}.",
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
            "Completed Ollama generation with model {Model}.",
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
        ValidatePrompt(prompt);
        ArgumentNullException.ThrowIfNull(policy);
        var current = _options.CurrentValue;
        _logger.LogInformation(
            "Starting streaming Ollama generation with model {Model}.",
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
            "Completed streaming Ollama generation with model {Model}.",
            current.Model);
    }

    /// <inheritdoc />
    public async Task<LlmHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var start = _timeProvider.GetTimestamp();
        var current = _options.CurrentValue;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(current.TimeoutSeconds));

        try
        {
            var models = await ListLocalModelsAsync(current, timeoutSource.Token).ConfigureAwait(false);
            var modelAvailable = IsConfiguredModelPresent(models, current.Model);
            var latency = GetElapsedMilliseconds(start);
            var error = modelAvailable
                ? null
                : $"The Ollama model '{current.Model}' is not installed.";

            _logger.LogInformation(
                "Ollama health check completed in {LatencyMs} ms. Model available: {ModelAvailable}.",
                latency,
                modelAvailable);
            return new LlmHealthResult(true, modelAvailable, latency, error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            var latency = GetElapsedMilliseconds(start);
            var error = $"Ollama did not respond within {current.TimeoutSeconds} seconds.";
            _logger.LogWarning(
                exception,
                "Ollama health check timed out after {LatencyMs} ms.",
                latency);
            return new LlmHealthResult(false, false, latency, error);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or LlmConfigurationException)
        {
            var latency = GetElapsedMilliseconds(start);
            _logger.LogWarning(
                exception,
                "Ollama health check failed after {LatencyMs} ms.",
                latency);
            return new LlmHealthResult(false, false, latency, exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListInstalledModelsAsync(
        string endpoint,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var current = _options.CurrentValue;
        var catalogOptions = current with
        {
            Endpoint = endpoint,
            TimeoutSeconds = timeoutSeconds,
        };
        var validation = LlmOptionsValidation.ValidateOllama(catalogOptions);
        if (validation is not null)
        {
            throw new LlmConfigurationException(validation);
        }

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var models = await ListLocalModelsAsync(
                catalogOptions,
                timeoutSource.Token).ConfigureAwait(false);
            return models
                .Select(static model => model.Name ?? model.ModelName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new OllamaUnavailableException(
                $"Ollama did not respond within {timeoutSeconds} seconds.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new OllamaUnavailableException(
                $"Ollama is unavailable at '{endpoint}'.",
                exception);
        }
    }

    internal async Task EnsureModelAvailableAsync(CancellationToken cancellationToken)
    {
        var current = _options.CurrentValue;
        var models = await ListLocalModelsAsync(current, cancellationToken).ConfigureAwait(false);
        if (!IsConfiguredModelPresent(models, current.Model))
        {
            throw new OllamaModelMissingException(current.Model);
        }
    }

    private async Task<IReadOnlyList<Model>> ListLocalModelsAsync(
        OllamaOptions options,
        CancellationToken cancellationToken)
    {
        var validation = LlmOptionsValidation.ValidateOllama(options);
        if (validation is not null)
        {
            throw new LlmConfigurationException(validation);
        }

        var httpClient = _httpClientFactory.CreateClient(LlmHttpClientNames.Ollama);
        httpClient.BaseAddress = LlmOptionsValidation.ParseEndpoint(
            options.Endpoint,
            OllamaOptions.SectionName);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        using var client = new OllamaApiClient(httpClient, options.Model);
        var models = await client.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.ToArray();
    }

    private static bool IsConfiguredModelPresent(IEnumerable<Model> models, string configuredModel)
    {
        return models.Any(model =>
            string.Equals(model.Name, configuredModel, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(model.ModelName, configuredModel, StringComparison.OrdinalIgnoreCase));
    }

    private long GetElapsedMilliseconds(long start)
    {
        return (long)Math.Round(
            _timeProvider.GetElapsedTime(start).TotalMilliseconds,
            MidpointRounding.AwayFromZero);
    }

    private static void ValidatePrompt(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
    }

    private EffectiveGenerationPolicy ResolveGlobalPolicy() =>
        GenerationPolicyResolver.Resolve(
            _aiOptions?.CurrentValue.GenerationDefaults ??
            new AIGenerationDefaults());
}
