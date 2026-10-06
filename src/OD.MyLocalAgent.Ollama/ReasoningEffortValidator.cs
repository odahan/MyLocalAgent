using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Ollama;

/// <summary>Probes reasoning-effort request syntax without inferring model vocabularies.</summary>
public sealed class ReasoningEffortValidator : IReasoningEffortValidator
{
    private const int MaximumDiagnosticCharacters = 8192;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<OpenAIOptions> _openAIOptions;
    private readonly ConcurrentDictionary<ValidationKey, ReasoningEffortValidationResult> _cache = new();

    /// <summary>Initializes the provider-aware validator.</summary>
    public ReasoningEffortValidator(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<AIOptions> aiOptions,
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<OpenAIOptions> openAIOptions)
    {
        _httpClientFactory = httpClientFactory;
        _aiOptions = aiOptions;
        _ollamaOptions = ollamaOptions;
        _openAIOptions = openAIOptions;
    }

    /// <inheritdoc />
    public ReasoningEffortValidationResult ValidateCached(string model, string reasoningEffort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (GenerationPolicyValidation.ValidateReasoningEffort(reasoningEffort) is { } error)
        {
            return new ReasoningEffortValidationResult(ReasoningEffortValidationStatus.Rejected, error);
        }

        return _cache.TryGetValue(CreateValidationKey(model, reasoningEffort), out var cached)
            ? cached
            : new ReasoningEffortValidationResult(
                ReasoningEffortValidationStatus.Unavailable,
                $"Reasoning level '{reasoningEffort}' has not been verified with model '{model}'. Compatibility will be determined when the task runs.");
    }

    /// <inheritdoc />
    public async Task<ReasoningEffortValidationResult> ValidateAsync(
        string model,
        string reasoningEffort,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (GenerationPolicyValidation.ValidateReasoningEffort(reasoningEffort) is { } error)
        {
            return new ReasoningEffortValidationResult(ReasoningEffortValidationStatus.Rejected, error);
        }

        var provider = _aiOptions.CurrentValue.Provider;
        var key = CreateValidationKey(model, reasoningEffort);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = string.Equals(provider, AIOptions.OllamaProvider, StringComparison.OrdinalIgnoreCase)
            ? await ValidateOllamaAsync(model, reasoningEffort, cancellationToken).ConfigureAwait(false)
            : await ValidateOpenAIAsync(model, reasoningEffort, cancellationToken).ConfigureAwait(false);
        if (result.Status != ReasoningEffortValidationStatus.Unavailable)
        {
            _cache.TryAdd(key, result);
        }

        return result;
    }

    private ValidationKey CreateValidationKey(string model, string reasoningEffort)
    {
        var provider = _aiOptions.CurrentValue.Provider;
        var endpoint = string.Equals(provider, AIOptions.OllamaProvider, StringComparison.OrdinalIgnoreCase)
            ? _ollamaOptions.CurrentValue.Endpoint
            : _openAIOptions.CurrentValue.Endpoint;
        return new ValidationKey(provider, endpoint, model, reasoningEffort);
    }

    private async Task<ReasoningEffortValidationResult> ValidateOllamaAsync(
        string model,
        string reasoningEffort,
        CancellationToken cancellationToken)
    {
        var options = _ollamaOptions.CurrentValue;
        var client = _httpClientFactory.CreateClient(LlmHttpClientNames.Ollama);
        client.BaseAddress = LlmOptionsValidation.ParseEndpoint(options.Endpoint, OllamaOptions.SectionName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = CreateJsonContent(new
            {
                model,
                messages = new[] { new { role = "user", content = "Reply with OK." } },
                stream = false,
                think = reasoningEffort,
                options = new { num_predict = 1 },
            }),
        };
        return await SendAsync(client, request, reasoningEffort, options.TimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ReasoningEffortValidationResult> ValidateOpenAIAsync(
        string model,
        string reasoningEffort,
        CancellationToken cancellationToken)
    {
        var options = _openAIOptions.CurrentValue;
        var validation = LlmOptionsValidation.ValidateOpenAI(options, requireApiKey: true);
        if (validation is not null)
        {
            return new ReasoningEffortValidationResult(ReasoningEffortValidationStatus.Unavailable, validation);
        }

        var client = _httpClientFactory.CreateClient(LlmHttpClientNames.OpenAI);
        client.BaseAddress = LlmOptionsValidation.ParseEndpoint(options.Endpoint, OpenAIOptions.SectionName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = CreateJsonContent(new
            {
                model,
                messages = new[] { new { role = "user", content = "Reply with OK." } },
                stream = false,
                reasoning_effort = reasoningEffort,
                max_completion_tokens = 16,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return await SendAsync(client, request, reasoningEffort, options.TimeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ReasoningEffortValidationResult> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        string reasoningEffort,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new ReasoningEffortValidationResult(
                    ReasoningEffortValidationStatus.Accepted,
                    $"Reasoning effort '{reasoningEffort}' was accepted.");
            }

            var diagnostic = await ReadDiagnosticAsync(response, timeoutSource.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity &&
                IsReasoningDiagnostic(diagnostic))
            {
                return new ReasoningEffortValidationResult(
                    ReasoningEffortValidationStatus.Rejected,
                    $"The model rejected reasoning effort '{reasoningEffort}'.");
            }

            return new ReasoningEffortValidationResult(
                ReasoningEffortValidationStatus.Unavailable,
                $"Reasoning-effort validation returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ReasoningEffortValidationResult(
                ReasoningEffortValidationStatus.Unavailable,
                "Reasoning-effort validation timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new ReasoningEffortValidationResult(
                ReasoningEffortValidationStatus.Unavailable,
                $"Reasoning-effort validation could not reach the provider: {exception.Message}");
        }
    }

    private static StringContent CreateJsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<string> ReadDiagnosticAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return text.Length <= MaximumDiagnosticCharacters ? text : text[..MaximumDiagnosticCharacters];
    }

    private static bool IsReasoningDiagnostic(string diagnostic) =>
        diagnostic.Contains("reasoning", StringComparison.OrdinalIgnoreCase) ||
        diagnostic.Contains("reasoning_effort", StringComparison.OrdinalIgnoreCase) ||
        diagnostic.Contains("think", StringComparison.OrdinalIgnoreCase) ||
        diagnostic.Contains("effort", StringComparison.OrdinalIgnoreCase);

    private sealed record ValidationKey(
        string Provider,
        string Endpoint,
        string Model,
        string ReasoningEffort);
}
