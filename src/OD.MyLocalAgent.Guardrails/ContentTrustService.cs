using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ContentTrustService>? _logger;
    private readonly ConcurrentDictionary<string, VerdictCacheEntry> _verdictCache = [];
    private readonly ConcurrentDictionary<string, SummaryCacheEntry> _summaryCache = [];
    private readonly ConcurrentDictionary<string, CompactSummaryCacheEntry> _compactSummaryCache = [];

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
    /// <param name="logger">The optional sanitized aggregate logger.</param>
    public ContentTrustService(
        ILlmClientFactory clientFactory,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IOptionsMonitor<AIOptions> aiOptions,
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<OpenAIOptions> openAIOptions,
        TimeProvider timeProvider,
        IExecutionDebugTrace? debugTrace = null,
        ILogger<ContentTrustService>? logger = null)
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
        _logger = logger;
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
            TaskTimingCollector.Current.Value?.Record(
                TaskTimingCollector.GuardrailCategory,
                "screen",
                _timeProvider.GetElapsedTime(operationStart));
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
            TaskTimingCollector.Current.Value?.Record(
                TaskTimingCollector.GuardrailCategory,
                "screen",
                _timeProvider.GetElapsedTime(operationStart));
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
        var summarizeStarted = _timeProvider.GetTimestamp();
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

            TaskTimingCollector.Current.Value?.Record(
                TaskTimingCollector.GuardrailCategory,
                "summarize",
                _timeProvider.GetElapsedTime(summarizeStarted));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            TaskTimingCollector.Current.Value?.Record(
                TaskTimingCollector.GuardrailCategory,
                "summarize",
                _timeProvider.GetElapsedTime(summarizeStarted));
            throw new GuardrailSummarizationException(
                $"Guardrail summarization failed for provider '{providerName}' ({exception.GetType().Name}).",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<CompactExternalContentBatchResult> ProcessCompactAsync(
        IReadOnlyList<CompactExternalContentItem> items,
        CancellationToken cancellationToken)
    {
        ValidateCompactItems(items);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _guardrailsOptions.CurrentValue;
        if (!options.ScreeningEnabled)
        {
            return new CompactExternalContentBatchResult(
                items.Select(CreateDisabledCompactResult).ToArray(),
                []);
        }

        var provider = _clientFactory.Create();
        var snapshot = CaptureSnapshot(provider);
        var operationStarted = _timeProvider.GetTimestamp();
        var defaults = _aiOptions.CurrentValue.GenerationDefaults;
        var screeningPolicy = GenerationPolicyResolver.ResolveInternal(
            InternalGenerationPolicies.GuardrailScreening,
            defaults);
        var reductionPolicy = GenerationPolicyResolver.ResolveInternal(
            InternalGenerationPolicies.GuardrailReduction,
            defaults);
        var results = new CompactExternalContentResult?[items.Count];
        var failures = new List<CompactExternalContentBatchFailure>();
        var partitions = PartitionCompactItems(items, options.MaxContentChars);
        var sourceCalls = 0;
        var summaryCalls = 0;
        var finalCalls = 0;
        var sourceCacheHits = 0;
        var summaryCacheHits = 0;
        var finalCacheHits = 0;
        long sourceLatencyMs = 0;
        long summaryLatencyMs = 0;
        long finalLatencyMs = 0;

        foreach (var batch in partitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchStarted = _timeProvider.GetTimestamp();
            var states = batch
                .Select(index => new CompactItemState(index, items[index]))
                .ToArray();

            foreach (var state in states.Where(static state => state.Item.Content.Length == 0))
            {
                var trust = CreateDeterministicTrust(string.Empty);
                const string emptySummary = "[No readable external content was available.]";
                results[state.Index] = new CompactExternalContentResult(
                    state.Item.Id,
                    emptySummary,
                    trust,
                    CreateDeterministicTrust(emptySummary),
                    CompactExternalContentStatus.Summarized,
                    false,
                    0);
            }

            var activeStates = states
                .Where(static state => state.Item.Content.Length > 0)
                .ToArray();
            if (activeStates.Length == 0)
            {
                continue;
            }

            try
            {
                EnsureSnapshot(snapshot);
                var misses = new List<CompactItemState>();
                foreach (var state in activeStates)
                {
                    var key = CreateCompactVerdictCacheKey(snapshot, state.Item.Content);
                    if (options.CacheResults && TryGetVerdictCache(key, out var cached))
                    {
                        sourceCacheHits++;
                        state.SourceVerdict = new VerdictCallResult(
                            cached.Score,
                            cached.Indicators,
                            0);
                    }
                    else
                    {
                        misses.Add(state);
                    }
                }

                if (misses.Count > 0)
                {
                    sourceCalls++;
                    var call = await CallProviderAsync(
                            provider,
                            snapshot,
                            CompactContentBatchScreenerPrompt.System,
                            BuildCompactScreeningRequest(misses),
                            options,
                            screeningPolicy,
                            requireStableConfiguration: true,
                            cancellationToken,
                            CalculateScreeningOutputTokens(misses.Count))
                        .ConfigureAwait(false);
                    sourceLatencyMs += call.LatencyMs;
                    if (call.Text.Length > CalculateScreeningResponseCharacterLimit(
                            misses.Select(static state => state.Item).ToArray()))
                    {
                        throw new FormatException(
                            "The compact screening response exceeded its strict bound.");
                    }

                    var parsed = ParseCompactVerdicts(
                        call.Text,
                        misses.Select(static state => state.Item).ToArray());
                    foreach (var state in misses)
                    {
                        var verdict = parsed[state.Item.Id];
                        state.SourceVerdict = new VerdictCallResult(
                            verdict.Score,
                            verdict.Indicators,
                            call.LatencyMs);
                        if (options.CacheResults)
                        {
                            _verdictCache[CreateCompactVerdictCacheKey(
                                snapshot,
                                state.Item.Content)] = new VerdictCacheEntry(
                                    verdict.Score,
                                    verdict.Indicators,
                                    _timeProvider.GetUtcNow());
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                var failure = CreateCompactFailure(
                    "SourceScreening",
                    snapshot,
                    activeStates.Length,
                    batchStarted);
                failures.Add(failure);
                foreach (var state in activeStates)
                {
                    results[state.Index] = new CompactExternalContentResult(
                        state.Item.Id,
                        null,
                        CreateFailedTrust(state.Item.Content, provider.ProviderName),
                        null,
                        CompactExternalContentStatus.SourceScreeningUnavailable,
                        true,
                        failure.Exception is CompactExternalContentPhaseException phase
                            ? phase.ElapsedMilliseconds
                            : 0);
                }

                continue;
            }

            var eligible = new List<CompactItemState>();
            foreach (var state in activeStates)
            {
                var verdict = state.SourceVerdict
                    ?? throw new InvalidOperationException("Compact source verdict is missing.");
                state.SourceTrust = CreateScreenedTrust(
                    state.Item.Content,
                    verdict,
                    provider.ProviderName);
                if (verdict.Score < options.CautionThreshold)
                {
                    results[state.Index] = new CompactExternalContentResult(
                        state.Item.Id,
                        null,
                        state.SourceTrust,
                        null,
                        CompactExternalContentStatus.SourceWithheld,
                        true,
                        verdict.LatencyMs);
                }
                else
                {
                    eligible.Add(state);
                }
            }

            if (eligible.Count == 0)
            {
                continue;
            }

            try
            {
                EnsureSnapshot(snapshot);
                var misses = new List<CompactItemState>();
                foreach (var state in eligible)
                {
                    var key = CreateCompactSummaryCacheKey(
                        snapshot,
                        state.Item.Content,
                        state.Item.MaximumSummaryCharacters);
                    if (options.CacheResults && TryGetCompactSummaryCache(key, out var summary))
                    {
                        summaryCacheHits++;
                        state.Summary = summary;
                    }
                    else
                    {
                        misses.Add(state);
                    }
                }

                if (misses.Count > 0)
                {
                    summaryCalls++;
                    var responseLimit = CalculateSummaryResponseCharacterLimit(misses);
                    var call = await CallProviderAsync(
                            provider,
                            snapshot,
                            CompactContentBatchSummarizerPrompt.System,
                            BuildCompactSummaryRequest(misses),
                            options,
                            reductionPolicy,
                            requireStableConfiguration: true,
                            cancellationToken,
                            CalculateSummaryOutputTokens(responseLimit))
                        .ConfigureAwait(false);
                    summaryLatencyMs += call.LatencyMs;
                    if (call.Text.Length > responseLimit)
                    {
                        throw new FormatException(
                            "The compact summary response exceeded its strict bound.");
                    }

                    var parsed = ParseCompactSummaries(
                        call.Text,
                        misses.Select(static state => state.Item).ToArray());
                    foreach (var state in misses)
                    {
                        state.Summary = parsed[state.Item.Id];
                        if (options.CacheResults)
                        {
                            _compactSummaryCache[CreateCompactSummaryCacheKey(
                                snapshot,
                                state.Item.Content,
                                state.Item.MaximumSummaryCharacters)] =
                                new CompactSummaryCacheEntry(
                                    state.Summary,
                                    _timeProvider.GetUtcNow());
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                var failure = CreateCompactFailure(
                    "Summarization",
                    snapshot,
                    eligible.Count,
                    batchStarted);
                failures.Add(failure);
                foreach (var state in eligible)
                {
                    results[state.Index] = new CompactExternalContentResult(
                        state.Item.Id,
                        null,
                        state.SourceTrust!,
                        null,
                        CompactExternalContentStatus.SummarizationUnavailable,
                        true,
                        state.SourceTrust!.LatencyMs + GetFailureLatency(failure));
                }

                continue;
            }

            try
            {
                EnsureSnapshot(snapshot);
                var misses = new List<CompactItemState>();
                foreach (var state in eligible)
                {
                    var summary = state.Summary
                        ?? throw new InvalidOperationException("Compact summary is missing.");
                    var key = CreateCompactVerdictCacheKey(snapshot, summary);
                    if (options.CacheResults && TryGetVerdictCache(key, out var cached))
                    {
                        finalCacheHits++;
                        state.SummaryVerdict = new VerdictCallResult(
                            cached.Score,
                            cached.Indicators,
                            0);
                    }
                    else
                    {
                        misses.Add(state);
                    }
                }

                if (misses.Count > 0)
                {
                    finalCalls++;
                    var summaryItems = misses.Select(
                        static state => new CompactExternalContentItem(
                            state.Item.Id,
                            state.Summary!,
                            state.Item.MaximumSummaryCharacters)).ToArray();
                    var call = await CallProviderAsync(
                            provider,
                            snapshot,
                            CompactContentBatchScreenerPrompt.System,
                            BuildCompactScreeningRequest(summaryItems),
                            options,
                            screeningPolicy,
                            requireStableConfiguration: true,
                            cancellationToken,
                            CalculateScreeningOutputTokens(misses.Count))
                        .ConfigureAwait(false);
                    finalLatencyMs += call.LatencyMs;
                    if (call.Text.Length > CalculateScreeningResponseCharacterLimit(summaryItems))
                    {
                        throw new FormatException(
                            "The compact final-screening response exceeded its strict bound.");
                    }

                    var parsed = ParseCompactVerdicts(call.Text, summaryItems);
                    foreach (var state in misses)
                    {
                        var verdict = parsed[state.Item.Id];
                        state.SummaryVerdict = new VerdictCallResult(
                            verdict.Score,
                            verdict.Indicators,
                            call.LatencyMs);
                        if (options.CacheResults)
                        {
                            _verdictCache[CreateCompactVerdictCacheKey(
                                snapshot,
                                state.Summary!)] = new VerdictCacheEntry(
                                    verdict.Score,
                                    verdict.Indicators,
                                    _timeProvider.GetUtcNow());
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                var failure = CreateCompactFailure(
                    "FinalScreening",
                    snapshot,
                    eligible.Count,
                    batchStarted);
                failures.Add(failure);
                foreach (var state in eligible)
                {
                    results[state.Index] = new CompactExternalContentResult(
                        state.Item.Id,
                        null,
                        state.SourceTrust!,
                        null,
                        CompactExternalContentStatus.FinalScreeningUnavailable,
                        true,
                        state.SourceTrust!.LatencyMs + GetFailureLatency(failure));
                }

                continue;
            }

            foreach (var state in eligible)
            {
                var sourceTrust = state.SourceTrust!;
                var summaryVerdict = state.SummaryVerdict!;
                var effectiveVerdict = new VerdictCallResult(
                    Math.Min(sourceTrust.TrustScore!.Value, summaryVerdict.Score),
                    UnionIndicators(
                        sourceTrust.SuspiciousIndicators,
                        summaryVerdict.Indicators),
                    summaryVerdict.LatencyMs);
                var summaryTrust = CreateScreenedTrust(
                    state.Summary!,
                    effectiveVerdict,
                    provider.ProviderName);
                results[state.Index] = new CompactExternalContentResult(
                    state.Item.Id,
                    state.Summary,
                    sourceTrust,
                    summaryTrust,
                    CompactExternalContentStatus.Summarized,
                    false,
                    sourceTrust.LatencyMs + summaryVerdict.LatencyMs);
            }
        }

        var completedResults = results.Select(static result => result
                ?? throw new InvalidOperationException("A compact item result is missing."))
                .ToArray();
        _logger?.LogInformation(
            "Compact Guardrails completed for provider {ProviderName}, model {ModelName}: items {ItemCount}, batches {BatchCount}, source calls {SourceCallCount}, summary calls {SummaryCallCount}, final calls {FinalCallCount}, source cache hits {SourceCacheHitCount}, summary cache hits {SummaryCacheHitCount}, final cache hits {FinalCacheHitCount}, screened {ScreenedCount}, summarized {SummarizedCount}, withheld {WithheldCount}, human review {ReviewCount}, source characters {SourceCharacterCount}, source latency {SourceLatencyMs} ms, summary latency {SummaryLatencyMs} ms, final latency {FinalLatencyMs} ms, total {TotalLatencyMs} ms.",
            snapshot.Provider,
            snapshot.Model,
            items.Count,
            partitions.Count,
            sourceCalls,
            summaryCalls,
            finalCalls,
            sourceCacheHits,
            summaryCacheHits,
            finalCacheHits,
            completedResults.Count(static result => result.SourceTrust.Screened),
            completedResults.Count(static result => result.Status == CompactExternalContentStatus.Summarized),
            completedResults.Count(static result => result.Status == CompactExternalContentStatus.SourceWithheld),
            completedResults.Count(static result => result.RequiresHumanReview),
            items.Sum(static item => item.Content.Length),
            sourceLatencyMs,
            summaryLatencyMs,
            finalLatencyMs,
            GetElapsedMilliseconds(operationStarted));
        TaskTimingCollector.Current.Value?.Record(
            TaskTimingCollector.GuardrailCategory,
            "compact",
            _timeProvider.GetElapsedTime(operationStarted));
        return new CompactExternalContentBatchResult(completedResults, failures);
    }

    /// <inheritdoc />
    public async Task<CompactExternalContentBatchResult> ScreenCompactAsync(
        IReadOnlyList<CompactExternalContentItem> items,
        CancellationToken cancellationToken)
    {
        ValidateCompactItems(items);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _guardrailsOptions.CurrentValue;
        if (!options.ScreeningEnabled)
        {
            return new CompactExternalContentBatchResult(
                items.Select(CreateDisabledCompactResult).ToArray(),
                []);
        }

        var provider = _clientFactory.Create();
        var snapshot = CaptureSnapshot(provider);
        var operationStarted = _timeProvider.GetTimestamp();
        var screeningPolicy = GenerationPolicyResolver.ResolveInternal(
            InternalGenerationPolicies.GuardrailScreening,
            _aiOptions.CurrentValue.GenerationDefaults);
        var results = new CompactExternalContentResult?[items.Count];
        var failures = new List<CompactExternalContentBatchFailure>();
        var partitions = PartitionCompactItems(items, options.MaxContentChars);
        var sourceCalls = 0;
        var sourceCacheHits = 0;
        long sourceLatencyMs = 0;

        foreach (var batch in partitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchStarted = _timeProvider.GetTimestamp();
            var states = batch
                .Select(index => new CompactItemState(index, items[index]))
                .ToArray();

            foreach (var state in states.Where(static state => state.Item.Content.Length == 0))
            {
                results[state.Index] = new CompactExternalContentResult(
                    state.Item.Id,
                    null,
                    CreateDeterministicTrust(string.Empty),
                    null,
                    CompactExternalContentStatus.Screened,
                    false,
                    0);
            }

            var activeStates = states
                .Where(static state => state.Item.Content.Length > 0)
                .ToArray();
            if (activeStates.Length == 0)
            {
                continue;
            }

            try
            {
                EnsureSnapshot(snapshot);
                var misses = new List<CompactItemState>();
                foreach (var state in activeStates)
                {
                    var key = CreateCompactVerdictCacheKey(snapshot, state.Item.Content);
                    if (options.CacheResults && TryGetVerdictCache(key, out var cached))
                    {
                        sourceCacheHits++;
                        state.SourceVerdict = new VerdictCallResult(
                            cached.Score,
                            cached.Indicators,
                            0);
                    }
                    else
                    {
                        misses.Add(state);
                    }
                }

                if (misses.Count > 0)
                {
                    sourceCalls++;
                    var call = await CallProviderAsync(
                            provider,
                            snapshot,
                            CompactContentBatchScreenerPrompt.System,
                            BuildCompactScreeningRequest(misses),
                            options,
                            screeningPolicy,
                            requireStableConfiguration: true,
                            cancellationToken,
                            CalculateScreeningOutputTokens(misses.Count))
                        .ConfigureAwait(false);
                    sourceLatencyMs += call.LatencyMs;
                    if (call.Text.Length > CalculateScreeningResponseCharacterLimit(
                            misses.Select(static state => state.Item).ToArray()))
                    {
                        throw new FormatException(
                            "The compact screening response exceeded its strict bound.");
                    }

                    var parsed = ParseCompactVerdicts(
                        call.Text,
                        misses.Select(static state => state.Item).ToArray());
                    foreach (var state in misses)
                    {
                        var verdict = parsed[state.Item.Id];
                        state.SourceVerdict = new VerdictCallResult(
                            verdict.Score,
                            verdict.Indicators,
                            call.LatencyMs);
                        if (options.CacheResults)
                        {
                            _verdictCache[CreateCompactVerdictCacheKey(
                                snapshot,
                                state.Item.Content)] = new VerdictCacheEntry(
                                    verdict.Score,
                                    verdict.Indicators,
                                    _timeProvider.GetUtcNow());
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                var failure = CreateCompactFailure(
                    "SourceScreening",
                    snapshot,
                    activeStates.Length,
                    batchStarted);
                failures.Add(failure);
                foreach (var state in activeStates)
                {
                    results[state.Index] = new CompactExternalContentResult(
                        state.Item.Id,
                        null,
                        CreateFailedTrust(state.Item.Content, provider.ProviderName),
                        null,
                        CompactExternalContentStatus.SourceScreeningUnavailable,
                        true,
                        GetFailureLatency(failure));
                }

                continue;
            }

            foreach (var state in activeStates)
            {
                var verdict = state.SourceVerdict
                    ?? throw new InvalidOperationException("Compact source verdict is missing.");
                var trust = CreateScreenedTrust(
                    state.Item.Content,
                    verdict,
                    provider.ProviderName);
                results[state.Index] = verdict.Score < options.CautionThreshold
                    ? new CompactExternalContentResult(
                        state.Item.Id,
                        null,
                        trust,
                        null,
                        CompactExternalContentStatus.SourceWithheld,
                        true,
                        verdict.LatencyMs)
                    : new CompactExternalContentResult(
                        state.Item.Id,
                        null,
                        trust,
                        null,
                        CompactExternalContentStatus.Screened,
                        false,
                        verdict.LatencyMs);
            }
        }

        var completedScreenResults = results.Select(static result => result
                ?? throw new InvalidOperationException("A compact item result is missing."))
                .ToArray();
        _logger?.LogInformation(
            "Screen-only compact Guardrails completed for provider {ProviderName}, model {ModelName}: items {ItemCount}, batches {BatchCount}, source calls {SourceCallCount}, source cache hits {SourceCacheHitCount}, screened {ScreenedCount}, withheld {WithheldCount}, human review {ReviewCount}, source characters {SourceCharacterCount}, source latency {SourceLatencyMs} ms, total {TotalLatencyMs} ms.",
            snapshot.Provider,
            snapshot.Model,
            items.Count,
            partitions.Count,
            sourceCalls,
            sourceCacheHits,
            completedScreenResults.Count(static result => result.Status == CompactExternalContentStatus.Screened),
            completedScreenResults.Count(static result => result.Status == CompactExternalContentStatus.SourceWithheld),
            completedScreenResults.Count(static result => result.RequiresHumanReview),
            items.Sum(static item => item.Content.Length),
            sourceLatencyMs,
            GetElapsedMilliseconds(operationStarted));
        TaskTimingCollector.Current.Value?.Record(
            TaskTimingCollector.GuardrailCategory,
            "screen-compact",
            _timeProvider.GetElapsedTime(operationStarted));
        return new CompactExternalContentBatchResult(completedScreenResults, failures);
    }

    private void ValidateCompactItems(IReadOnlyList<CompactExternalContentItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException(
                "At least one compact external-content item is required.",
                nameof(items));
        }

        var options = _guardrailsOptions.CurrentValue;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException(
                    "Compact external-content items cannot contain null entries.",
                    nameof(items));
            }

            if (string.IsNullOrWhiteSpace(item.Id))
            {
                throw new ArgumentException(
                    "Every compact external-content item requires a non-empty id.",
                    nameof(items));
            }

            if (!ids.Add(item.Id))
            {
                throw new ArgumentException(
                    "Compact external-content item ids must be unique.",
                    nameof(items));
            }

            if (item.Content is null || item.Content.Length > options.MaxContentChars)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items),
                    $"Compact content cannot exceed {options.MaxContentChars} characters.");
            }

            if (item.MaximumSummaryCharacters < 1 ||
                item.MaximumSummaryCharacters > options.MaxSummaryChars)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items),
                    $"Compact summary sizes must be between 1 and {options.MaxSummaryChars} characters.");
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<int>> PartitionCompactItems(
        IReadOnlyList<CompactExternalContentItem> items,
        int maximumItemCharacters)
    {
        const int maximumItems = 12;
        var maximumCharacters = 6L * maximumItemCharacters;
        var batches = new List<IReadOnlyList<int>>();
        var current = new List<int>();
        long characters = 0;
        for (var index = 0; index < items.Count; index++)
        {
            var itemCharacters = items[index].Content.Length;
            if (current.Count > 0 &&
                (current.Count == maximumItems ||
                 characters + itemCharacters > maximumCharacters))
            {
                batches.Add(current.ToArray());
                current = [];
                characters = 0;
            }

            current.Add(index);
            characters += itemCharacters;
        }

        batches.Add(current.ToArray());
        return batches;
    }

    private static string BuildCompactScreeningRequest(
        IReadOnlyList<CompactItemState> states) =>
        BuildCompactScreeningRequest(
            states.Select(static state => state.Item).ToArray());

    private static string BuildCompactScreeningRequest(
        IReadOnlyList<CompactExternalContentItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Classify every independent item below.");
        foreach (var item in items)
        {
            var nonce = CreateRequestNonce();
            builder.Append("Item id: ").AppendLine(JsonSerializer.Serialize(item.Id));
            builder.Append("<<<UNTRUSTED-CONTENT ").Append(nonce).AppendLine(">>>");
            builder.AppendLine(item.Content);
            builder.Append("<<<END ").Append(nonce).AppendLine(">>>");
        }

        return builder.ToString();
    }

    private static string BuildCompactSummaryRequest(
        IReadOnlyList<CompactItemState> states)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Summarize every independent eligible item below.");
        foreach (var state in states)
        {
            var nonce = CreateRequestNonce();
            builder.Append("Item id: ").AppendLine(JsonSerializer.Serialize(state.Item.Id));
            builder.Append("Maximum summary characters: ")
                .AppendLine(state.Item.MaximumSummaryCharacters.ToString(CultureInfo.InvariantCulture));
            builder.Append("<<<UNTRUSTED-SOURCE-DATA ").Append(nonce).AppendLine(">>>");
            builder.AppendLine(state.Item.Content);
            builder.Append("<<<END-UNTRUSTED-SOURCE-DATA ").Append(nonce).AppendLine(">>>");
        }

        return builder.ToString();
    }

    private static IReadOnlyDictionary<string, ParsedVerdict> ParseCompactVerdicts(
        string response,
        IReadOnlyList<CompactExternalContentItem> expectedItems)
    {
        var root = ParseStrictBatchRoot(response);
        var byId = expectedItems.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        var parsed = new Dictionary<string, ParsedVerdict>(StringComparer.Ordinal);
        foreach (var result in root.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object ||
                result.EnumerateObject().Count() != 3 ||
                !result.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String ||
                !result.TryGetProperty("score", out var scoreElement) ||
                scoreElement.ValueKind != JsonValueKind.Number ||
                !scoreElement.TryGetInt32(out var score) ||
                score is < 0 or > 100 ||
                !result.TryGetProperty("indicators", out var indicatorsElement) ||
                indicatorsElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("A compact screening result has an invalid schema.");
            }

            var id = idElement.GetString()!;
            if (!byId.TryGetValue(id, out var expected) || !parsed.TryAdd(id, null!))
            {
                throw new FormatException("A compact screening result contains an invalid id set.");
            }

            var indicators = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var indicatorElement in indicatorsElement.EnumerateArray())
            {
                if (indicatorElement.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("Compact screening indicators must be strings.");
                }

                var indicator = indicatorElement.GetString()!;
                if (!string.IsNullOrEmpty(indicator) &&
                    expected.Content.Contains(indicator, StringComparison.Ordinal) &&
                    seen.Add(indicator) &&
                    indicators.Count < 20)
                {
                    indicators.Add(indicator);
                }
            }

            parsed[id] = new ParsedVerdict(score, indicators);
        }

        if (parsed.Count != expectedItems.Count)
        {
            throw new FormatException("A compact screening response omitted an expected id.");
        }

        return parsed;
    }

    private static IReadOnlyDictionary<string, string> ParseCompactSummaries(
        string response,
        IReadOnlyList<CompactExternalContentItem> expectedItems)
    {
        var root = ParseStrictBatchRoot(response);
        var byId = expectedItems.ToDictionary(static item => item.Id, StringComparer.Ordinal);
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in root.EnumerateArray())
        {
            if (result.ValueKind != JsonValueKind.Object ||
                result.EnumerateObject().Count() != 2 ||
                !result.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String ||
                !result.TryGetProperty("summary", out var summaryElement) ||
                summaryElement.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("A compact summary result has an invalid schema.");
            }

            var id = idElement.GetString()!;
            var summary = summaryElement.GetString()!;
            if (!byId.TryGetValue(id, out var expected) ||
                string.IsNullOrWhiteSpace(summary) ||
                summary.Length > expected.MaximumSummaryCharacters ||
                !parsed.TryAdd(id, summary))
            {
                throw new FormatException("A compact summary result is incomplete or invalid.");
            }
        }

        if (parsed.Count != expectedItems.Count)
        {
            throw new FormatException("A compact summary response omitted an expected id.");
        }

        return parsed;
    }

    private static JsonElement ParseStrictBatchRoot(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new FormatException("The compact Guardrails response is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("The compact Guardrails response root is invalid.");
            }

            return results.Clone();
        }
        catch (JsonException exception)
        {
            throw new FormatException("The compact Guardrails response is malformed.", exception);
        }
    }

    private CompactExternalContentBatchFailure CreateCompactFailure(
        string phase,
        ProviderSnapshot snapshot,
        int itemCount,
        long batchStarted)
    {
        var exception = new CompactExternalContentPhaseException(
            phase,
            snapshot.Provider,
            snapshot.Model,
            itemCount,
            GetElapsedMilliseconds(batchStarted));
        return new CompactExternalContentBatchFailure(phase, exception, itemCount);
    }

    private static long GetFailureLatency(CompactExternalContentBatchFailure failure) =>
        failure.Exception is CompactExternalContentPhaseException exception
            ? exception.ElapsedMilliseconds
            : 0;

    private static CompactExternalContentResult CreateDisabledCompactResult(
        CompactExternalContentItem item) =>
        new(
            item.Id,
            null,
            new ContentTrustResult(
                null,
                ContentTrustEnvelope.WrapBase64(item.Content),
                "base64",
                ContentTrustStatus.ScreeningDisabled,
                [],
                "Disabled",
                0),
            null,
            CompactExternalContentStatus.SourceScreeningUnavailable,
            true,
            0);

    private static ContentTrustResult CreateDeterministicTrust(string content) =>
        new(
            100,
            ContentTrustEnvelope.WrapPlaintext(content),
            "none",
            ContentTrustStatus.Screened,
            [],
            "Deterministic",
            0);

    private static ContentTrustResult CreateScreenedTrust(
        string content,
        VerdictCallResult verdict,
        string providerName) =>
        new(
            verdict.Score,
            ContentTrustEnvelope.WrapPlaintext(content),
            "none",
            ContentTrustStatus.Screened,
            verdict.Indicators,
            providerName,
            verdict.LatencyMs);

    private static ContentTrustResult CreateFailedTrust(
        string content,
        string providerName) =>
        new(
            null,
            ContentTrustEnvelope.WrapBase64(content),
            "base64",
            ContentTrustStatus.Failed,
            [],
            providerName,
            0);

    private static IReadOnlyList<string> UnionIndicators(
        IEnumerable<string> first,
        IEnumerable<string> second)
    {
        var result = new List<string>();
        AddIndicators(result, first);
        AddIndicators(result, second);
        return result;
    }

    private static int CalculateScreeningOutputTokens(int itemCount) =>
        Math.Max(256, itemCount * 192);

    private static int CalculateScreeningResponseCharacterLimit(
        IReadOnlyList<CompactExternalContentItem> items) =>
        checked(256 + items.Sum(static item => item.Id.Length + 512));

    private static int CalculateSummaryResponseCharacterLimit(
        IReadOnlyList<CompactItemState> states) =>
        checked(
            32 + states.Sum(
                static state => state.Item.MaximumSummaryCharacters +
                    state.Item.Id.Length + 48));

    private static int CalculateSummaryOutputTokens(int responseCharacterLimit) =>
        Math.Max(256, checked((responseCharacterLimit + 1) / 2));

    private bool TryGetCompactSummaryCache(string key, out string summary)
    {
        if (_compactSummaryCache.TryGetValue(key, out var entry))
        {
            if (_timeProvider.GetUtcNow() - entry.CreatedAt <= CacheLifetime)
            {
                summary = entry.Summary;
                return true;
            }

            _compactSummaryCache.TryRemove(key, out _);
        }

        summary = string.Empty;
        return false;
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
        CancellationToken cancellationToken,
        int? maxOutputTokens = null)
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
                provider.ProviderName,
                "guardrail");
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
                        MaxOutputTokens = maxOutputTokens,
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

    private static string CreateCompactVerdictCacheKey(
        ProviderSnapshot snapshot,
        string content) =>
        Hash(
            $"{CompactContentBatchScreenerPrompt.Version}\n{snapshot.Provider}\n{snapshot.Model}\n{Hash(content)}");

    private static string CreateCompactSummaryCacheKey(
        ProviderSnapshot snapshot,
        string content,
        int maxSummaryChars) =>
        Hash(
            $"{CompactContentBatchSummarizerPrompt.Version}\n{snapshot.Provider}\n{snapshot.Model}\n" +
            $"{maxSummaryChars}\n{Hash(content)}");

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

    private sealed record CompactSummaryCacheEntry(
        string Summary,
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

    private sealed class CompactItemState(
        int index,
        CompactExternalContentItem item)
    {
        public int Index { get; } = index;

        public CompactExternalContentItem Item { get; } = item;

        public VerdictCallResult? SourceVerdict { get; set; }

        public ContentTrustResult? SourceTrust { get; set; }

        public string? Summary { get; set; }

        public VerdictCallResult? SummaryVerdict { get; set; }
    }
}
