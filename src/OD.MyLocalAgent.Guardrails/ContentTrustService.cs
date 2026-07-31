using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Guardrails;

/// <summary>
/// Provides tool-less prompt-injection classification and complete-source reduction.
/// </summary>
public sealed class ContentTrustService : IContentTrustService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private const int MaximumReductionLevels = 64;

    private readonly ILlmClientFactory _clientFactory;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<OpenAIOptions> _openAIOptions;
    private readonly TimeProvider _timeProvider;
    private readonly IExecutionDebugTrace _debugTrace;
    private readonly ConcurrentDictionary<string, VerdictCacheEntry> _verdictCache = [];
    private readonly ConcurrentDictionary<string, SummaryCacheEntry> _summaryCache = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentTrustService"/> class.
    /// </summary>
    /// <param name="clientFactory">The hot-switching provider factory.</param>
    /// <param name="guardrailsOptions">The reloadable guardrail settings.</param>
    /// <param name="aiOptions">The reloadable provider selection.</param>
    /// <param name="ollamaOptions">The reloadable Ollama settings.</param>
    /// <param name="openAIOptions">The reloadable OpenAI-compatible settings.</param>
    /// <param name="timeProvider">The time source for latency, timeout, and cache expiry.</param>
    /// <param name="debugTrace">The optional ephemeral interactive diagnostic trace.</param>
    public ContentTrustService(
        ILlmClientFactory clientFactory,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IOptionsMonitor<AIOptions> aiOptions,
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<OpenAIOptions> openAIOptions,
        TimeProvider timeProvider,
        IExecutionDebugTrace? debugTrace = null)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(ollamaOptions);
        ArgumentNullException.ThrowIfNull(openAIOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _clientFactory = clientFactory;
        _guardrailsOptions = guardrailsOptions;
        _aiOptions = aiOptions;
        _ollamaOptions = ollamaOptions;
        _openAIOptions = openAIOptions;
        _timeProvider = timeProvider;
        _debugTrace = debugTrace ?? new NullExecutionDebugTrace();
    }

    /// <inheritdoc />
    public async Task<ContentTrustResult> ScreenAsync(
        string content,
        bool returnBase64,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var options = _guardrailsOptions.CurrentValue;
        if (!options.ScreeningEnabled)
        {
            return new ContentTrustResult(
                null,
                ContentTrustEnvelope.WrapBase64(content),
                "base64",
                ContentTrustStatus.ScreeningDisabled,
                [],
                "Disabled",
                0);
        }

        if (content.Length > options.MaxContentChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(content),
                $"Screening input cannot exceed {options.MaxContentChars} characters.");
        }

        var operationStart = _timeProvider.GetTimestamp();
        var providerName = GetConfiguredProviderName();
        try
        {
            var provider = _clientFactory.Create();
            providerName = provider.ProviderName;
            var snapshot = CaptureSnapshot(provider);
            var screeningPolicy = GenerationPolicyResolver.ResolveInternal(
                InternalGenerationPolicies.GuardrailScreening,
                _aiOptions.CurrentValue.GenerationDefaults);
            var verdict = await GetVerdictAsync(
                    provider,
                    snapshot,
                    content,
                    options,
                    screeningPolicy,
                    requireStableConfiguration: false,
                    cancellationToken)
                .ConfigureAwait(false);
            var wrapped = returnBase64
                ? ContentTrustEnvelope.WrapBase64(content)
                : ContentTrustEnvelope.WrapPlaintext(content);
            return new ContentTrustResult(
                verdict.Score,
                wrapped,
                returnBase64 ? "base64" : "none",
                ContentTrustStatus.Screened,
                verdict.Indicators,
                providerName,
                verdict.LatencyMs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var fallback = new ContentTrustResult(
                null,
                ContentTrustEnvelope.WrapBase64(content),
                "base64",
                ContentTrustStatus.Failed,
                [],
                providerName,
                GetElapsedMilliseconds(operationStart));
            throw new GuardrailScreeningException(
                $"Guardrail screening failed for provider '{providerName}' ({exception.GetType().Name}).",
                exception,
                fallback);
        }
    }

    /// <inheritdoc />
    public async Task<ContentSummaryResult> SummarizeAsync(
        string content,
        int maxSummaryChars,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var options = _guardrailsOptions.CurrentValue;
        if (maxSummaryChars < 1 || maxSummaryChars > options.MaxSummaryChars)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSummaryChars),
                $"Summary length must be between 1 and {options.MaxSummaryChars} characters.");
        }

        if (!options.ScreeningEnabled)
        {
            return CreateEmptySummary(
                ContentSummaryStatus.LlmProcessingDisabled,
                content.Length,
                "Disabled");
        }

        if (!options.SummarizeOversizedContent)
        {
            return CreateEmptySummary(
                ContentSummaryStatus.SummarizationDisabled,
                content.Length,
                "Disabled");
        }

        if (content.Length > options.MaxSummarizationSourceChars)
        {
            return CreateEmptySummary(
                ContentSummaryStatus.SourceTooLarge,
                content.Length,
                "Disabled");
        }

        var providerName = GetConfiguredProviderName();
        try
        {
            var provider = _clientFactory.Create();
            providerName = provider.ProviderName;
            var snapshot = CaptureSnapshot(provider);
            var defaults = _aiOptions.CurrentValue.GenerationDefaults;
            var screeningPolicy = GenerationPolicyResolver.ResolveInternal(
                InternalGenerationPolicies.GuardrailScreening,
                defaults);
            var reductionPolicy = GenerationPolicyResolver.ResolveInternal(
                InternalGenerationPolicies.GuardrailReduction,
                defaults);
            var cacheKey = CreateSummaryCacheKey(snapshot, content, maxSummaryChars);
            if (options.CacheResults &&
                TryGetSummaryCache(cacheKey, out var cached))
            {
                return cached with { LatencyMs = 0 };
            }

            var result = await ReduceAsync(
                    provider,
                    snapshot,
                    content,
                    maxSummaryChars,
                    options,
                    screeningPolicy,
                    reductionPolicy,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureSnapshot(snapshot);
            if (options.CacheResults)
            {
                _summaryCache[cacheKey] = new SummaryCacheEntry(
                    result,
                    _timeProvider.GetUtcNow());
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GuardrailSummarizationException(
                $"Guardrail summarization failed for provider '{providerName}' ({exception.GetType().Name}).",
                exception);
        }
    }

    private async Task<ContentSummaryResult> ReduceAsync(
        ILlmClientService provider,
        ProviderSnapshot snapshot,
        string content,
        int maxSummaryChars,
        GuardrailsOptions options,
        EffectiveGenerationPolicy screeningPolicy,
        EffectiveGenerationPolicy reductionPolicy,
        CancellationToken cancellationToken)
    {
        var currentLevel = content;
        var observedInputs = new HashSet<string>(StringComparer.Ordinal);
        var indicators = new List<string>();
        var minimumScore = 100;
        long totalLatency = 0;

        for (var level = 0; level < MaximumReductionLevels; level++)
        {
            var levelHash = Hash(currentLevel);
            if (!observedInputs.Add(levelHash))
            {
                throw new InvalidOperationException(
                    "The guardrail reducer did not make deterministic progress.");
            }

            var chunks = SplitCompleteContent(currentLevel, options.MaxContentChars);
            var summaries = new List<string>(chunks.Count);
            foreach (var chunk in chunks)
            {
                var verdict = await GetVerdictAsync(
                        provider,
                        snapshot,
                        chunk,
                        options,
                        screeningPolicy,
                        requireStableConfiguration: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                minimumScore = Math.Min(minimumScore, verdict.Score);
                AddIndicators(indicators, verdict.Indicators);
                totalLatency += verdict.LatencyMs;

                var summaryCall = await CallProviderAsync(
                        provider,
                        snapshot,
                        ContentSummarizerPrompt.System,
                        WrapForReduction(chunk),
                        options,
                        reductionPolicy,
                        requireStableConfiguration: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                summaries.Add(ParseSummary(summaryCall.Text));
                totalLatency += summaryCall.LatencyMs;
            }

            if (summaries.Count == 1)
            {
                var capped = CapSummary(summaries[0], maxSummaryChars);
                var status = capped.Truncated
                    ? ContentSummaryStatus.SummaryTruncated
                    : ContentSummaryStatus.Summarized;
                return new ContentSummaryResult(
                    status,
                    capped.Summary,
                    content.Length,
                    capped.Summary.Length,
                    minimumScore,
                    indicators.ToArray(),
                    provider.ProviderName,
                    totalLatency);
            }

            currentLevel = string.Join(Environment.NewLine, summaries);
        }

        throw new InvalidOperationException(
            "The guardrail reducer exceeded its safe reduction depth.");
    }

    private async Task<VerdictCallResult> GetVerdictAsync(
        ILlmClientService provider,
        ProviderSnapshot snapshot,
        string content,
        GuardrailsOptions options,
        EffectiveGenerationPolicy generationPolicy,
        bool requireStableConfiguration,
        CancellationToken cancellationToken)
    {
        var key = CreateVerdictCacheKey(snapshot, content);
        if (options.CacheResults && TryGetVerdictCache(key, out var cached))
        {
            if (requireStableConfiguration)
            {
                EnsureSnapshot(snapshot);
            }

            return new VerdictCallResult(
                cached.Score,
                cached.Indicators,
                0);
        }

        var call = await CallProviderAsync(
                provider,
                snapshot,
                ContentScreenerPrompt.System,
                WrapForScreening(content),
                options,
                generationPolicy,
                requireStableConfiguration,
                cancellationToken)
            .ConfigureAwait(false);
        var verdict = ParseVerdict(call.Text, content);
        var stable = IsCurrentSnapshot(snapshot);
        if (requireStableConfiguration && !stable)
        {
            throw new InvalidOperationException(
                "The active provider or model changed during guardrail processing.");
        }

        if (options.CacheResults && stable)
        {
            _verdictCache[key] = new VerdictCacheEntry(
                verdict.Score,
                verdict.Indicators,
                _timeProvider.GetUtcNow());
        }

        return new VerdictCallResult(
            verdict.Score,
            verdict.Indicators,
            call.LatencyMs);
    }

    private async Task<ProviderCallResult> CallProviderAsync(
        ILlmClientService provider,
        ProviderSnapshot snapshot,
        string systemPrompt,
        string userPrompt,
        GuardrailsOptions options,
        EffectiveGenerationPolicy generationPolicy,
        bool requireStableConfiguration,
        CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        using var timeoutSource = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.TimeoutSeconds),
            _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            var policyClient = new GenerationPolicyChatClient(
                provider.ChatClient,
                generationPolicy,
                provider.ProviderName);
            WriteLlmRequestTrace(
                provider.ProviderName,
                systemPrompt,
                userPrompt,
                generationPolicy,
                options.TimeoutSeconds);
            var response = await policyClient.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, userPrompt),
                    ],
                    new ChatOptions
                    {
                        Tools = null,
                    },
                    linkedSource.Token)
                .ConfigureAwait(false);
            if (requireStableConfiguration)
            {
                EnsureSnapshot(snapshot);
            }

            WriteLlmResponseTrace(provider.ProviderName, response.Text);
            return new ProviderCallResult(
                response.Text,
                GetElapsedMilliseconds(started));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The guardrail provider call exceeded {options.TimeoutSeconds} seconds.",
                exception);
        }
    }

    private ProviderSnapshot CaptureSnapshot(ILlmClientService provider)
    {
        var configuredProvider = _aiOptions.CurrentValue.Provider;
        if (!string.Equals(
                configuredProvider,
                provider.ProviderName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected provider changed while guardrail processing started.");
        }

        return new ProviderSnapshot(
            provider.ProviderName,
            GetCurrentModel(provider.ProviderName));
    }

    private void EnsureSnapshot(ProviderSnapshot snapshot)
    {
        if (!IsCurrentSnapshot(snapshot))
        {
            throw new InvalidOperationException(
                "The active provider or model changed during guardrail processing.");
        }
    }

    private bool IsCurrentSnapshot(ProviderSnapshot snapshot) =>
        string.Equals(
            _aiOptions.CurrentValue.Provider,
            snapshot.Provider,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            GetCurrentModel(snapshot.Provider),
            snapshot.Model,
            StringComparison.Ordinal);

    private string GetCurrentModel(string provider) =>
        string.Equals(provider, AIOptions.OpenAIProvider, StringComparison.OrdinalIgnoreCase)
            ? _openAIOptions.CurrentValue.Model
            : _ollamaOptions.CurrentValue.Model;

    private string GetConfiguredProviderName()
    {
        var provider = _aiOptions.CurrentValue.Provider;
        return string.IsNullOrWhiteSpace(provider) ? "Unknown" : provider;
    }

    private void WriteLlmRequestTrace(
        string provider,
        string systemPrompt,
        string userPrompt,
        EffectiveGenerationPolicy generationPolicy,
        int timeoutSeconds)
    {
        if (_debugTrace.IsEnabled)
        {
            _debugTrace.Write(
                "llm.request",
                "guardrails",
                new
                {
                    provider,
                    timeoutSeconds,
                    generation = new
                    {
                        generationPolicy.Temperature,
                        generationPolicy.TopP,
                        generationPolicy.ContextWindowTokens,
                    },
                    systemPrompt,
                    userPrompt,
                });
        }
    }

    private void WriteLlmResponseTrace(string provider, string responseText)
    {
        if (_debugTrace.IsEnabled)
        {
            _debugTrace.Write(
                "llm.response",
                "guardrails",
                new { provider, responseText });
        }
    }

    private bool TryGetVerdictCache(string key, out VerdictCacheEntry entry)
    {
        if (_verdictCache.TryGetValue(key, out entry!))
        {
            if (_timeProvider.GetUtcNow() - entry.CreatedAt <= CacheLifetime)
            {
                return true;
            }

            _verdictCache.TryRemove(key, out _);
        }

        entry = default!;
        return false;
    }

    private bool TryGetSummaryCache(string key, out ContentSummaryResult result)
    {
        if (_summaryCache.TryGetValue(key, out var entry))
        {
            if (_timeProvider.GetUtcNow() - entry.CreatedAt <= CacheLifetime)
            {
                result = entry.Result;
                return true;
            }

            _summaryCache.TryRemove(key, out _);
        }

        result = default!;
        return false;
    }

    private long GetElapsedMilliseconds(long started) =>
        (long)Math.Round(
            _timeProvider.GetElapsedTime(started).TotalMilliseconds,
            MidpointRounding.AwayFromZero);

    private static ContentSummaryResult CreateEmptySummary(
        ContentSummaryStatus status,
        int originalCharacterCount,
        string providerName) =>
        new(
            status,
            string.Empty,
            originalCharacterCount,
            0,
            null,
            [],
            providerName,
            0);

    private static IReadOnlyList<string> SplitCompleteContent(
        string content,
        int maximumLength)
    {
        if (content.Length == 0)
        {
            return [string.Empty];
        }

        var chunks = new List<string>();
        var offset = 0;
        while (offset < content.Length)
        {
            var remaining = content.Length - offset;
            if (remaining <= maximumLength)
            {
                chunks.Add(content[offset..]);
                break;
            }

            var boundary = FindPreferredBoundary(
                content.AsSpan(offset, maximumLength));
            chunks.Add(content.Substring(offset, boundary));
            offset += boundary;
        }

        return chunks;
    }

    private static int FindPreferredBoundary(ReadOnlySpan<char> candidate)
    {
        var lineBreak = candidate.LastIndexOf('\n');
        if (lineBreak >= 0)
        {
            return lineBreak + 1;
        }

        for (var index = candidate.Length - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(candidate[index]))
            {
                return index + 1;
            }
        }

        return candidate.Length;
    }

    private static string WrapForScreening(string content)
    {
        var nonce = CreateRequestNonce();
        return
            $"<<<UNTRUSTED-CONTENT {nonce}>>>{Environment.NewLine}" +
            $"{content}{Environment.NewLine}" +
            $"<<<END {nonce}>>>";
    }

    private static string WrapForReduction(string content)
    {
        var nonce = CreateRequestNonce();
        return
            $"<<<UNTRUSTED-SOURCE-DATA {nonce}>>>{Environment.NewLine}" +
            $"{content}{Environment.NewLine}" +
            $"<<<END-UNTRUSTED-SOURCE-DATA {nonce}>>>";
    }

    private static string CreateRequestNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static ParsedVerdict ParseVerdict(string response, string content)
    {
        var root = ParseJsonObject(response);
        if (!root.TryGetProperty("score", out var scoreElement) ||
            !root.TryGetProperty("indicators", out var indicatorsElement) ||
            indicatorsElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException(
                "The guardrail verdict is missing required properties.");
        }

        var rawScore = scoreElement.GetRawText();
        if (scoreElement.ValueKind != JsonValueKind.Number ||
            !long.TryParse(
                rawScore,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsedScore))
        {
            throw new FormatException(
                "The guardrail verdict score must be a JSON integer.");
        }

        var indicators = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in indicatorsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new FormatException(
                    "The guardrail verdict indicators must be strings.");
            }

            var indicator = item.GetString()!;
            if (indicators.Count < 20 &&
                !string.IsNullOrEmpty(indicator) &&
                content.Contains(indicator, StringComparison.Ordinal) &&
                seen.Add(indicator))
            {
                indicators.Add(indicator);
            }
        }

        var clampedScore = (int)Math.Clamp(parsedScore, 0L, 100L);
        return new ParsedVerdict(clampedScore, indicators.ToArray());
    }

    private static string ParseSummary(string response)
    {
        var root = ParseJsonObject(response);
        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != 1 ||
            properties[0].Name != "summary" ||
            properties[0].Value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException(
                "The guardrail summary must contain only one string summary property.");
        }

        var summary = properties[0].Value.GetString();
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new FormatException(
                "The guardrail summary cannot be empty.");
        }

        return summary;
    }

    private static JsonElement ParseJsonObject(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new FormatException("The guardrail response is empty.");
        }

        try
        {
            using var direct = JsonDocument.Parse(response);
            if (direct.RootElement.ValueKind == JsonValueKind.Object)
            {
                return direct.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
        }

        var extracted = ExtractFirstBalancedObject(response);
        if (extracted is null)
        {
            throw new FormatException(
                "The guardrail response does not contain a JSON object.");
        }

        try
        {
            using var document = JsonDocument.Parse(extracted);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException(
                    "The guardrail response root must be a JSON object.");
            }

            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new FormatException(
                "The guardrail response contains malformed JSON.",
                exception);
        }
    }

    private static string? ExtractFirstBalancedObject(string value)
    {
        for (var start = value.IndexOf('{'); start >= 0; start = value.IndexOf('{', start + 1))
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var index = start; index < value.Length; index++)
            {
                var character = value[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                }
                else if (character == '{')
                {
                    depth++;
                }
                else if (character == '}' && --depth == 0)
                {
                    return value[start..(index + 1)];
                }
            }
        }

        return null;
    }

    private static CappedSummary CapSummary(string summary, int maximumLength)
    {
        if (summary.Length <= maximumLength)
        {
            return new CappedSummary(summary, false);
        }

        var omitted = summary.Length - maximumLength;
        string marker;
        int prefixLength;
        do
        {
            marker = $"[... AI summary truncated by {omitted} characters ...]";
            prefixLength = Math.Max(0, maximumLength - marker.Length);
            var updatedOmitted = summary.Length - prefixLength;
            if (updatedOmitted == omitted)
            {
                break;
            }

            omitted = updatedOmitted;
        }
        while (true);

        marker = $"[... AI summary truncated by {omitted} characters ...]";
        if (marker.Length > maximumLength)
        {
            marker = marker[..maximumLength];
        }

        prefixLength = maximumLength - marker.Length;
        return new CappedSummary(
            summary[..prefixLength] + marker,
            true);
    }

    private static void AddIndicators(
        List<string> target,
        IEnumerable<string> candidates)
    {
        foreach (var indicator in candidates)
        {
            if (target.Count >= 20)
            {
                return;
            }

            if (!target.Contains(indicator, StringComparer.Ordinal))
            {
                target.Add(indicator);
            }
        }
    }

    private static string CreateVerdictCacheKey(
        ProviderSnapshot snapshot,
        string content) =>
        Hash(
            $"{ContentScreenerPrompt.Version}\n{snapshot.Provider}\n{snapshot.Model}\n{Hash(content)}");

    private static string CreateSummaryCacheKey(
        ProviderSnapshot snapshot,
        string content,
        int maxSummaryChars) =>
        Hash(
            $"{ContentScreenerPrompt.Version}\n{ContentSummarizerPrompt.Version}\n" +
            $"{snapshot.Provider}\n{snapshot.Model}\n{maxSummaryChars}\n{Hash(content)}");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private readonly record struct ProviderSnapshot(string Provider, string Model);

    private sealed record VerdictCacheEntry(
        int Score,
        IReadOnlyList<string> Indicators,
        DateTimeOffset CreatedAt);

    private sealed record SummaryCacheEntry(
        ContentSummaryResult Result,
        DateTimeOffset CreatedAt);

    private sealed record ParsedVerdict(
        int Score,
        IReadOnlyList<string> Indicators);

    private sealed record ProviderCallResult(string Text, long LatencyMs);

    private sealed record VerdictCallResult(
        int Score,
        IReadOnlyList<string> Indicators,
        long LatencyMs);

    private sealed record CappedSummary(string Summary, bool Truncated);
}
