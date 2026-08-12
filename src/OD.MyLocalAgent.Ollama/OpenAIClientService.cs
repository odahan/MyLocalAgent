using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Diagnostics;
using System.Text;

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
    public ProviderCapabilities Capabilities
    {
        get
        {
            var current = _options.CurrentValue;
            var capabilities = ProviderCapabilities.TextChat;
            if (current.SupportsImageInput)
            {
                capabilities |= ProviderCapabilities.ImageInput;
            }

            if (current.SupportsImageOutput)
            {
                capabilities |= ProviderCapabilities.ImageOutput;
            }

            if (current.SupportsAudioOutput)
            {
                capabilities |= ProviderCapabilities.AudioOutput;
            }

            return capabilities;
        }
    }

    /// <inheritdoc />
    public async Task<MultimodalGenerationResult> GenerateMediaAsync(
        MultimodalGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        var current = GetValidatedOptions();
        var required = request.Kind == MultimodalGenerationKind.Image
            ? ProviderCapabilities.ImageOutput
            : ProviderCapabilities.AudioOutput;
        if (!Capabilities.HasFlag(required))
        {
            throw new NotSupportedException(
                $"The configured OpenAI-compatible endpoint has not declared {request.Kind} output support.");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting OpenAI {MediaKind} generation with model {Model}.",
            request.Kind,
            request.Kind == MultimodalGenerationKind.Image ? current.ImageModel : current.AudioModel);
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(current.TimeoutSeconds));
        try
        {
            var result = request.Kind switch
            {
                MultimodalGenerationKind.Image => await GenerateImageAsync(
                        current,
                        request.Prompt,
                        timeoutSource.Token)
                    .ConfigureAwait(false),
                MultimodalGenerationKind.Audio => await GenerateAudioAsync(
                        current,
                        request.Prompt,
                        timeoutSource.Token)
                    .ConfigureAwait(false),
                _ => throw new NotSupportedException("The requested media kind is not implemented."),
            };
            _logger.LogInformation(
                "Completed OpenAI {MediaKind} generation in {ElapsedMs} ms with {SizeBytes} bytes.",
                request.Kind,
                stopwatch.ElapsedMilliseconds,
                result.Data.LongLength);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Canceled OpenAI {MediaKind} generation after {ElapsedMs} ms.",
                request.Kind,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "OpenAI {MediaKind} generation failed after {ElapsedMs} ms.",
                request.Kind,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

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

    private async Task<MultimodalGenerationResult> GenerateImageAsync(
        OpenAIOptions options,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var response = await SendMediaRequestAsync(
                options,
                "images/generations",
                new { model = options.ImageModel, prompt, n = 1 },
                cancellationToken)
            .ConfigureAwait(false);
        var json = await ReadLimitedAsync(response.Content, 36 * 1024 * 1024, cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0 ||
            !data[0].TryGetProperty("b64_json", out var encoded) ||
            string.IsNullOrWhiteSpace(encoded.GetString()))
        {
            throw new JsonException("The image-generation response did not contain image data.");
        }

        var bytes = Convert.FromBase64String(encoded.GetString()!);
        EnsureGeneratedMediaSize(bytes);
        return new MultimodalGenerationResult
        {
            Kind = MultimodalGenerationKind.Image,
            MediaType = "image/png",
            FileName = $"openai-image-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.png",
            Data = bytes,
        };
    }

    private async Task<MultimodalGenerationResult> GenerateAudioAsync(
        OpenAIOptions options,
        string prompt,
        CancellationToken cancellationToken)
    {
        using var response = await SendMediaRequestAsync(
                options,
                "audio/speech",
                new
                {
                    model = options.AudioModel,
                    input = prompt,
                    voice = options.AudioVoice,
                    response_format = "wav",
                },
                cancellationToken)
            .ConfigureAwait(false);
        var bytes = await ReadLimitedAsync(response.Content, 25 * 1024 * 1024, cancellationToken)
            .ConfigureAwait(false);
        return new MultimodalGenerationResult
        {
            Kind = MultimodalGenerationKind.Audio,
            MediaType = "audio/wav",
            FileName = $"openai-speech-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.wav",
            Data = bytes,
            Duration = TryGetWaveDuration(bytes),
        };
    }

    private async Task<HttpResponseMessage> SendMediaRequestAsync(
        OpenAIOptions options,
        string relativePath,
        object body,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(LlmHttpClientNames.OpenAI);
        client.BaseAddress = LlmOptionsValidation.ParseEndpoint(options.Endpoint, OpenAIOptions.SectionName);
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException(
                $"The provider media endpoint returned HTTP {(int)status} ({status}).",
                null,
                status);
        }

        return response;
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidOperationException("The generated media response exceeds the configured local limit.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var target = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            if (target.Length + count > maximumBytes)
            {
                throw new InvalidOperationException("The generated media response exceeds the configured local limit.");
            }

            target.Write(buffer, 0, count);
        }

        return target.ToArray();
    }

    private static void EnsureGeneratedMediaSize(byte[] data)
    {
        if (data.LongLength > 25 * 1024 * 1024)
        {
            throw new InvalidOperationException("The generated media exceeds the 25 MiB provider adapter limit.");
        }
    }

    private static TimeSpan? TryGetWaveDuration(byte[] data)
    {
        if (data.Length < 44 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF")
        {
            return null;
        }

        var byteRate = BitConverter.ToInt32(data, 28);
        var dataSize = BitConverter.ToInt32(data, 40);
        return byteRate > 0 && dataSize >= 0
            ? TimeSpan.FromSeconds((double)dataSize / byteRate)
            : null;
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
