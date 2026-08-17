using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Discovers web documents with deterministic collection and late protected delivery.
/// </summary>
[ToolGroup(
    "research",
    "Web Discovery",
    "Discover web documents using bounded search themes.",
    1200,
    Activation = ToolActivationKind.Singleton,
    ServiceContract = typeof(IResearchDiscoverTool))]
public sealed class ResearchDiscoverTool : IResearchDiscoverTool
{
    private const string TruncationMarker = "[truncated by OD.MyLocalAgent]";
    private const int MaximumSearchRequestsPerRun = 8;
    private const int MaximumThemesPerRun = 8;
    private static readonly int[] SlidingWindows = [2, 4, 7];
    private static readonly TimeSpan FutureDateTolerance = TimeSpan.FromMinutes(5);

    private readonly IWebSearchConnector _webSearchConnector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<HttpOptions> _httpOptions;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IContentTrustService _contentTrustService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IResearchTranslationService _translationService;
    private readonly ILogger<ResearchDiscoverTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResearchDiscoverTool"/> class.
    /// </summary>
    public ResearchDiscoverTool(
        IWebSearchConnector webSearchConnector,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<HttpOptions> httpOptions,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IContentTrustService contentTrustService,
        IIncidentReporter incidentReporter,
        IResearchTranslationService translationService,
        ILogger<ResearchDiscoverTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(webSearchConnector);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(httpOptions);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(contentTrustService);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        ArgumentNullException.ThrowIfNull(translationService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _webSearchConnector = webSearchConnector;
        _httpClientFactory = httpClientFactory;
        _httpOptions = httpOptions;
        _guardrailsOptions = guardrailsOptions;
        _contentTrustService = contentTrustService;
        _incidentReporter = incidentReporter;
        _translationService = translationService;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Tool("research.discover", "Discovers web documents matching search themes with bounded search and retrieval.", 100)]
    public Task<string> DiscoverAsync(
        [ToolArgument(WorkflowArgumentKind.String, "JSON array of search themes.", Example = "[{\"terms\":[\".NET\",\"C#\"],\"includeTerms\":[\"release\"]}]", IsLongText = true)] string themes,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Freshness window mode: 'sliding' for 2→4→7 day expansion, 'none' for no date filter.", AllowedValues = ["sliding", "none"], Example = "sliding")] string freshnessWindow = "sliding",
        [ToolArgument(WorkflowArgumentKind.String, "BCP-47 source language tag for search filtering.", Example = "en")] string sourceLanguage = "en",
        [ToolArgument(WorkflowArgumentKind.String, "BCP-47 target language for translation, or empty for original language.", Example = "fr")] string language = "",
        [ToolArgument(WorkflowArgumentKind.String, "JSON array of preferred domains for ranking.", Example = "[\"learn.microsoft.com\"]", IsLongText = true)] string preferredDomains = "[]",
        [ToolArgument(WorkflowArgumentKind.Integer, "Minimum unique dated documents target (1-100), ignored in 'none' mode.", Example = "8")] int minimumDocuments = 8,
        [ToolArgument(WorkflowArgumentKind.Integer, "Advanced compatibility setting: maximum search hits returned per request (1-10).", Example = "10")] int maxResultsPerQuery = 10,
        [ToolArgument(WorkflowArgumentKind.Integer, "Advanced compatibility safeguard: maximum page fetch attempts for the entire run (1-100).", Example = "40")] int maxRetrievalsPerRun = 40,
        CancellationToken cancellationToken = default)
    {
        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "research.discover",
            new
            {
                themeCount = themes?.Length > 0 ? ParseThemes(themes).Count : 0,
                freshnessWindow,
                sourceLanguage,
                targetLanguage = string.IsNullOrEmpty(language) ? "(original)" : language,
                minimumDocuments,
                maxResultsPerQuery,
                maxRetrievalsPerRun,
            },
            async () =>
            {
                var parsedThemes = ParseThemes(themes ?? "[]");
                if (parsedThemes.Count > MaximumThemesPerRun)
                {
                    throw new ArgumentException(
                        $"Web Discovery accepts at most {MaximumThemesPerRun} themes per run.",
                        nameof(themes));
                }

                var parsedDomains = ParseDomains(preferredDomains);
                var mode = ParseFreshnessWindow(freshnessWindow);
                var effectiveMaxPerQuery = Math.Clamp(maxResultsPerQuery, 1, 10);
                var effectiveMaxRetrievals = Math.Clamp(maxRetrievalsPerRun, 1, 100);
                var effectiveMinDocs = Math.Clamp(minimumDocuments, 1, 100);
                var effectiveLanguage = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
                var effectiveSourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage)
                    ? "en"
                    : sourceLanguage.Trim();
                var localNow = _timeProvider.GetLocalNow();
                var guardrails = _guardrailsOptions.CurrentValue;
                if (guardrails.MaxContentChars < 2)
                {
                    throw new InvalidOperationException(
                        "Guardrails:MaxContentChars must be at least 2 for Web Discovery.");
                }

                return mode == FreshnessWindow.Sliding
                    ? await RunSlidingModeAsync(
                        parsedThemes,
                        effectiveSourceLanguage,
                        effectiveLanguage,
                        parsedDomains,
                        effectiveMinDocs,
                        effectiveMaxPerQuery,
                        effectiveMaxRetrievals,
                        localNow,
                        guardrails,
                        cancellationToken).ConfigureAwait(false)
                    : await RunNoneModeAsync(
                        parsedThemes,
                        effectiveSourceLanguage,
                        effectiveLanguage,
                        parsedDomains,
                        effectiveMaxPerQuery,
                        effectiveMaxRetrievals,
                        guardrails,
                        cancellationToken).ConfigureAwait(false);
            },
            SummarizeResult);
    }

    private async Task<string> RunSlidingModeAsync(
        IReadOnlyList<Theme> themes,
        string sourceLanguage,
        string? targetLanguage,
        IReadOnlyList<string> preferredDomains,
        int minimumDocuments,
        int maxResultsPerQuery,
        int maxRetrievalsPerRun,
        DateTimeOffset localNow,
        GuardrailsOptions guardrails,
        CancellationToken cancellationToken)
    {
        var state = new DiscoveryCollectionState();
        var nowUtc = localNow.ToUniversalTime();
        var oldestWindowDays = SlidingWindows[^1];
        var earliestLocalDate = DateOnly.FromDateTime(localNow.DateTime).AddDays(-oldestWindowDays);
        var oldestWindowStartUtc = GetStartOfLocalDateUtc(earliestLocalDate);
        var primaryQueries = QueryCompiler.Compile(
            themes,
            sourceLanguage,
            earliestLocalDate,
            RemainingSearchRequests(state));
        var primaryResults = await ExecuteQueriesAsync(
                state,
                primaryQueries,
                maxResultsPerQuery,
                cancellationToken)
            .ConfigureAwait(false);
        await RetrieveRoundRobinAsync(
                state,
                primaryResults,
                maxRetrievalsPerRun,
                cancellationToken)
            .ConfigureAwait(false);

        if (CountEligibleDocuments(state.Documents.Values, oldestWindowStartUtc, nowUtc) < minimumDocuments)
        {
            foreach (var primaryResult in primaryResults.Where(static result => result.Succeeded))
            {
                if (state.SearchStopped ||
                    RemainingSearchRequests(state) == 0 ||
                    state.RetrievalsUsed >= maxRetrievalsPerRun)
                {
                    break;
                }

                var fallback = QueryCompiler.CompileFallback(
                    themes[primaryResult.CompiledQuery.ThemeIndex],
                    primaryResult.CompiledQuery.ThemeIndex,
                    sourceLanguage,
                    earliestLocalDate);
                var fallbackResult = await ExecuteQueryAsync(
                        state,
                        fallback,
                        maxResultsPerQuery,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (fallbackResult is not null)
                {
                    await RetrieveRoundRobinAsync(
                            state,
                            [fallbackResult],
                            maxRetrievalsPerRun,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (CountEligibleDocuments(state.Documents.Values, oldestWindowStartUtc, nowUtc) >= minimumDocuments)
                {
                    break;
                }
            }
        }

        var effectiveWindowDays = oldestWindowDays;
        var windowStartUtc = oldestWindowStartUtc;
        IReadOnlyList<WebDocument> eligible = [];
        foreach (var windowDays in SlidingWindows)
        {
            windowStartUtc = GetStartOfLocalDateUtc(
                DateOnly.FromDateTime(localNow.DateTime).AddDays(-windowDays));
            eligible = state.Documents.Values
                .Where(document => IsEligibleForWindow(document, windowStartUtc, nowUtc))
                .ToArray();
            effectiveWindowDays = windowDays;
            var minimumReached = eligible.Count >= minimumDocuments;
            var isFinalWindow = windowDays == oldestWindowDays;
            state.WindowHistory.Add(new
            {
                windowDays,
                windowStartUtc = windowStartUtc.ToString("O"),
                eligibleCandidateCount = eligible.Count,
                stopReason = state.SearchRateLimited
                    ? "searchRateLimited"
                    : minimumReached
                        ? "minimumReached"
                        : !isFinalWindow
                            ? "broaderWindowRequired"
                            : state.RetrievalsUsed >= maxRetrievalsPerRun
                                ? "retrievalBudgetExhausted"
                                : RemainingSearchRequests(state) == 0
                                    ? "searchRequestLimitReached"
                                    : "queriesExhausted",
            });
            if (minimumReached || state.SearchRateLimited)
            {
                break;
            }
        }

        var rejectedTooOld = state.Documents.Values.Count(document =>
            document.PublicationTime.HasValue && document.PublicationTime.Value < windowStartUtc);
        var rejectedFuture = state.Documents.Values.Count(document =>
            document.PublicationTime.HasValue &&
            document.PublicationTime.Value > nowUtc + FutureDateTolerance);
        var rejectedUnknown = state.Documents.Values.Count(document =>
            document.DateStatus == PublicationDateStatus.Unknown || !document.PublicationTime.HasValue);
        var insufficient = eligible.Count < minimumDocuments;

        return await BuildProtectedResultAsync(
                "sliding",
                state,
                eligible,
                preferredDomains,
                targetLanguage,
                guardrails,
                effectiveWindowDays,
                windowStartUtc,
                rejectedTooOld,
                rejectedUnknown,
                rejectedFuture,
                insufficient,
                minimumDocuments,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> RunNoneModeAsync(
        IReadOnlyList<Theme> themes,
        string sourceLanguage,
        string? targetLanguage,
        IReadOnlyList<string> preferredDomains,
        int maxResultsPerQuery,
        int maxRetrievalsPerRun,
        GuardrailsOptions guardrails,
        CancellationToken cancellationToken)
    {
        var state = new DiscoveryCollectionState();
        var primaryQueries = QueryCompiler.Compile(
            themes,
            sourceLanguage,
            null,
            RemainingSearchRequests(state));
        var primaryResults = await ExecuteQueriesAsync(
                state,
                primaryQueries,
                maxResultsPerQuery,
                cancellationToken)
            .ConfigureAwait(false);
        await RetrieveRoundRobinAsync(
                state,
                primaryResults,
                maxRetrievalsPerRun,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var primaryResult in primaryResults)
        {
            if (!primaryResult.Succeeded ||
                HasDocumentForQuery(state, primaryResult.CompiledQuery.Query) ||
                state.SearchStopped ||
                RemainingSearchRequests(state) == 0 ||
                state.RetrievalsUsed >= maxRetrievalsPerRun)
            {
                continue;
            }

            var fallback = QueryCompiler.CompileFallback(
                themes[primaryResult.CompiledQuery.ThemeIndex],
                primaryResult.CompiledQuery.ThemeIndex,
                sourceLanguage,
                null);
            var fallbackResult = await ExecuteQueryAsync(
                    state,
                    fallback,
                    maxResultsPerQuery,
                    cancellationToken)
                .ConfigureAwait(false);
            if (fallbackResult is not null)
            {
                await RetrieveRoundRobinAsync(
                        state,
                        [fallbackResult],
                        maxRetrievalsPerRun,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return await BuildProtectedResultAsync(
                "none",
                state,
                state.Documents.Values.ToArray(),
                preferredDomains,
                targetLanguage,
                guardrails,
                null,
                null,
                0,
                0,
                0,
                false,
                0,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RetrieveRoundRobinAsync(
        DiscoveryCollectionState state,
        IReadOnlyList<QuerySearchResult> queryResults,
        int retrievalLimit,
        CancellationToken cancellationToken)
    {
        var maximumRank = queryResults.Count == 0
            ? 0
            : queryResults.Max(static result => result.SearchResults.Count);
        for (var rank = 0; rank < maximumRank; rank++)
        {
            foreach (var queryResult in queryResults)
            {
                if (state.RetrievalsUsed >= retrievalLimit)
                {
                    return;
                }

                if (rank >= queryResult.SearchResults.Count)
                {
                    continue;
                }

                var hit = queryResult.SearchResults[rank];
                if (!TryGetSupportedAbsoluteUrl(hit.Url, out var supportedUrl))
                {
                    continue;
                }

                var canonicalUrl = UrlCanonicalizer.Canonicalize(supportedUrl);
                if (!state.AttemptedUrls.Add(canonicalUrl))
                {
                    AddQueryAssociation(state, canonicalUrl, queryResult.CompiledQuery.Query);
                    continue;
                }

                state.RetrievalsUsed++;
                var document = await RetrieveDocumentAsync(
                        hit,
                        canonicalUrl,
                        queryResult.CompiledQuery.Query,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (document is null)
                {
                    continue;
                }

                if (state.Documents.TryGetValue(document.CanonicalUrl, out var existing))
                {
                    state.Documents[document.CanonicalUrl] = existing with
                    {
                        DiscoveryQueryIds = existing.DiscoveryQueryIds
                            .Concat(document.DiscoveryQueryIds)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                    };
                }
                else
                {
                    state.Documents[document.CanonicalUrl] = document;
                }
            }
        }
    }

    private async Task<IReadOnlyList<QuerySearchResult>> ExecuteQueriesAsync(
        DiscoveryCollectionState state,
        IReadOnlyList<CompiledQuery> queries,
        int maxResultsPerQuery,
        CancellationToken cancellationToken)
    {
        var results = new List<QuerySearchResult>();
        foreach (var query in queries)
        {
            if (state.SearchStopped || RemainingSearchRequests(state) == 0)
            {
                break;
            }

            var result = await ExecuteQueryAsync(
                    state,
                    query,
                    maxResultsPerQuery,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    private async Task<QuerySearchResult?> ExecuteQueryAsync(
        DiscoveryCollectionState state,
        CompiledQuery query,
        int maxResultsPerQuery,
        CancellationToken cancellationToken)
    {
        if (state.SearchStopped || RemainingSearchRequests(state) == 0)
        {
            return null;
        }

        QuerySearchResult result;
        try
        {
            var searchResult = await _webSearchConnector
                .SearchAsync(query.Query, maxResultsPerQuery, cancellationToken)
                .ConfigureAwait(false);
            state.SuccessfulSearchQueryCount++;
            result = new QuerySearchResult(query, searchResult.Items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WebSearchApiException exception)
        {
            state.SearchFailureCount++;
            state.LastSearchErrorStatusCode = exception.StatusCode;
            state.SearchRateLimited |= exception.StatusCode == (int)HttpStatusCode.TooManyRequests;
            state.SearchStopped |= state.SearchRateLimited ||
                exception.StatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden;
            _logger.LogWarning(
                "Query execution failed for '{Query}': {ExceptionType} (HTTP {StatusCode})",
                query.Query,
                exception.GetType().Name,
                exception.StatusCode);
            result = new QuerySearchResult(query, [], false, exception.StatusCode);
        }
        catch (Exception exception)
        {
            state.SearchFailureCount++;
            _logger.LogWarning(
                "Query execution failed for '{Query}': {ExceptionType}",
                query.Query,
                exception.GetType().Name);
            result = new QuerySearchResult(query, [], false);
        }

        state.GeneratedQueries.Add(new
        {
            query = result.CompiledQuery.Query,
            themeIndex = result.CompiledQuery.ThemeIndex,
            intent = result.CompiledQuery.Intent.ToString(),
            resultCount = result.SearchResults.Count,
            succeeded = result.Succeeded,
            errorStatusCode = result.ErrorStatusCode,
        });
        return result;
    }

    private static int RemainingSearchRequests(DiscoveryCollectionState state) =>
        Math.Max(0, MaximumSearchRequestsPerRun - state.GeneratedQueries.Count);

    private static bool HasDocumentForQuery(DiscoveryCollectionState state, string query) =>
        state.Documents.Values.Any(document =>
            document.DiscoveryQueryIds.Contains(query, StringComparer.Ordinal));

    private static void AddQueryAssociation(
        DiscoveryCollectionState state,
        string canonicalUrl,
        string query)
    {
        if (state.Documents.TryGetValue(canonicalUrl, out var existing))
        {
            state.Documents[canonicalUrl] = existing with
            {
                DiscoveryQueryIds = existing.DiscoveryQueryIds
                    .Append(query)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            };
        }
    }

    private async Task<WebDocument?> RetrieveDocumentAsync(
        WebSearchResultItem hit,
        string canonicalUrl,
        string queryId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FetchDocumentAsync(hit, canonicalUrl, queryId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Document retrieval failed for '{Domain}': {ExceptionType}",
                UrlCanonicalizer.ExtractDomain(hit.Url),
                exception.GetType().Name);
            return null;
        }
    }

    private async Task<WebDocument?> FetchDocumentAsync(
        WebSearchResultItem hit,
        string canonicalUrl,
        string queryId,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(
            TimeSpan.FromSeconds(_httpOptions.CurrentValue.TimeoutSeconds),
            _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        var client = _httpClientFactory.CreateClient(HttpTool.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, hit.Url);
        if (!string.IsNullOrWhiteSpace(_httpOptions.CurrentValue.UserAgent))
        {
            request.Headers.UserAgent.ParseAdd(_httpOptions.CurrentValue.UserAgent);
        }

        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linkedSource.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(linkedSource.Token)
            .ConfigureAwait(false);
        if (body.Length > _httpOptions.CurrentValue.MaxResponseCharacters)
        {
            body = body[.._httpOptions.CurrentValue.MaxResponseCharacters];
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? hit.Url;
        var canonicalFinalUrl = UrlCanonicalizer.Canonicalize(finalUrl);
        var publicationTime = ExtractPublicationDate(body, mediaType);
        return new WebDocument(
            Guid.NewGuid().ToString("N"),
            hit.Url,
            finalUrl,
            canonicalFinalUrl.Length == 0 ? canonicalUrl : canonicalFinalUrl,
            ExtractTitle(body, mediaType) ?? hit.Title,
            UrlCanonicalizer.ExtractDomain(finalUrl),
            ClassifyDocumentKind(finalUrl, mediaType),
            publicationTime,
            publicationTime.HasValue ? PublicationDateStatus.Verified : PublicationDateStatus.Unknown,
            ExtractSummary(body, mediaType),
            mediaType ?? "text/html",
            [queryId],
            _timeProvider.GetUtcNow());
    }

    private async Task<string> BuildProtectedResultAsync(
        string mode,
        DiscoveryCollectionState state,
        IReadOnlyList<WebDocument> eligibleCandidates,
        IReadOnlyList<string> preferredDomains,
        string? targetLanguage,
        GuardrailsOptions guardrails,
        int? effectiveWindowDays,
        DateTimeOffset? windowStartUtc,
        int rejectedTooOld,
        int rejectedUnknown,
        int rejectedFuture,
        bool insufficient,
        int minimumDocuments,
        CancellationToken cancellationToken)
    {
        var ordered = SortAndPrioritize(eligibleCandidates, preferredDomains);
        var budgeted = BuildBudgetedPayload(ordered, guardrails.MaxContentChars);
        var finalBudget = budgeted;
        var trust = await ScreenWithFallbackAsync(
                budgeted.CanonicalPayload,
                "research.discover final source payload",
                cancellationToken)
            .ConfigureAwait(false);
        var disposition = ContentTrustPolicy.Decide(trust, guardrails);
        var delivery = GuardrailDelivery.Apply(budgeted.CanonicalPayload, trust, guardrails);
        var screeningCallCount = 1;
        var translationRequested = !string.IsNullOrEmpty(targetLanguage);
        var translationApplied = false;

        if (translationRequested &&
            disposition == TrustDisposition.PassThrough &&
            budgeted.Items.Count > 0)
        {
            try
            {
                var translated = await _translationService.TranslateAsync(
                        delivery.Content,
                        budgeted.Items.Select(static item => item.Id).ToArray(),
                        targetLanguage!,
                        cancellationToken)
                    .ConfigureAwait(false);
                var translatedCandidates = translated.Select(item =>
                    new ExternalCandidateContent(item.Id, item.Title, item.ShortSummary)).ToArray();
                var translatedBudget = BuildBudgetedPayload(
                    translatedCandidates,
                    guardrails.MaxContentChars);
                var translatedTrust = await ScreenWithFallbackAsync(
                        translatedBudget.CanonicalPayload,
                        "research.discover final translated payload",
                        cancellationToken)
                    .ConfigureAwait(false);
                screeningCallCount++;
                var translatedDisposition = ContentTrustPolicy.Decide(translatedTrust, guardrails);
                var translatedDelivery = GuardrailDelivery.Apply(
                    translatedBudget.CanonicalPayload,
                    translatedTrust,
                    guardrails);
                finalBudget = translatedBudget;
                trust = translatedTrust;
                disposition = translatedDisposition;
                delivery = translatedDelivery;
                translationApplied = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Research candidate translation failed: {ExceptionType}",
                    exception.GetType().Name);
            }
        }

        var deliverExternalData = disposition != TrustDisposition.Withhold;
        HashSet<string> deliveredIds = deliverExternalData
            ? finalBudget.Items.Select(static item => item.Id).ToHashSet(StringComparer.Ordinal)
            : [];
        var deliveredCandidates = deliverExternalData
            ? ordered.Where(candidate => deliveredIds.Contains(candidate.HitId)).ToArray()
            : [];
        var searchStatus = state.SearchRateLimited
            ? "rateLimited"
            : state.SearchFailureCount > 0 && state.SuccessfulSearchQueryCount == 0
                ? "unavailable"
                : state.SearchFailureCount > 0
                    ? "degraded"
                    : "available";
        var output = new Dictionary<string, object?>
        {
            ["freshnessWindow"] = mode,
            ["effectiveWindowDays"] = effectiveWindowDays,
            ["windowStartUtc"] = windowStartUtc?.ToString("O"),
            ["queries"] = state.GeneratedQueries,
            ["queryCount"] = state.GeneratedQueries.Count,
            ["searchRequestLimit"] = MaximumSearchRequestsPerRun,
            ["successfulSearchQueryCount"] = state.SuccessfulSearchQueryCount,
            ["searchFailureCount"] = state.SearchFailureCount,
            ["searchStatus"] = searchStatus,
            ["searchErrorStatusCode"] = state.LastSearchErrorStatusCode,
            ["windowHistory"] = state.WindowHistory,
            ["retrievalsUsed"] = state.RetrievalsUsed,
            ["discoveredCandidateCount"] = state.Documents.Count,
            ["eligibleCandidateCount"] = ordered.Count,
            ["candidateCount"] = deliveredCandidates.Length,
            ["datedCount"] = ordered.Count(candidate =>
                candidate.DateStatus == PublicationDateStatus.Verified && candidate.PublicationTime.HasValue),
            ["unknownDateCount"] = ordered.Count(candidate =>
                candidate.DateStatus == PublicationDateStatus.Unknown || !candidate.PublicationTime.HasValue),
            ["rejectedTooOldCount"] = rejectedTooOld,
            ["rejectedUnknownDateCount"] = rejectedUnknown,
            ["rejectedFutureDateCount"] = rejectedFuture,
            ["omittedBecauseOfBudget"] = ordered.Count - finalBudget.Items.Count,
            ["truncated"] = finalBudget.Truncated,
            ["candidates"] = deliveredCandidates.Select(candidate => new
            {
                hitId = candidate.HitId,
                sourceUrl = candidate.SourceUrl,
                finalUrl = candidate.FinalUrl,
                canonicalUrl = candidate.CanonicalUrl,
                domain = candidate.Domain,
                kind = candidate.Kind.ToString(),
                publicationTime = candidate.PublicationTime?.ToString("O"),
                dateStatus = candidate.DateStatus.ToString(),
                contentType = candidate.ContentType,
                discoveryQueryIds = candidate.DiscoveryQueryIds,
                retrievalTimeUtc = candidate.RetrievalTimeUtc.ToString("O"),
            }),
            ["protectedResults"] = delivery.Content,
            ["guardrails"] = new
            {
                screened = trust.Screened,
                trustStatus = trust.Status.ToString(),
                trustScore = trust.TrustScore,
                suspiciousIndicatorCount = trust.SuspiciousIndicators.Count,
                disposition = disposition.ToString(),
                contentEncoding = delivery.ContentEncoding,
                screeningCallCount,
            },
            ["translation"] = new
            {
                requested = translationRequested,
                applied = translationApplied,
                targetLanguage,
            },
        };

        if (disposition == TrustDisposition.Withhold)
        {
            output["withheld"] = $"score {trust.TrustScore}";
        }

        if (insufficient)
        {
            output["insufficient"] = true;
            output["insufficientReason"] = searchStatus switch
            {
                "rateLimited" =>
                    "The web-search provider rejected the discovery request because its request limit was reached (HTTP 429). Source availability could not be determined.",
                "unavailable" =>
                    "Every attempted web-search query failed. Source availability could not be determined.",
                _ =>
                    $"Only {ordered.Count} eligible dated documents were found in the {effectiveWindowDays}-day window, below the minimum threshold of {minimumDocuments}.",
            };
        }

        return ToolJson.Serialize(output);
    }

    private async Task<ContentTrustResult> ScreenWithFallbackAsync(
        string content,
        string incidentContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _contentTrustService
                .ScreenAsync(content, returnBase64: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GuardrailScreeningException exception)
        {
            _ = await _incidentReporter
                .ReportAsync(exception, incidentContext, CancellationToken.None)
                .ConfigureAwait(false);
            return exception.FallbackResult;
        }
    }

    private static BudgetedPayload BuildBudgetedPayload(
        IReadOnlyList<WebDocument> candidates,
        int maximumCharacters) =>
        BuildBudgetedPayload(
            candidates.Select(candidate => new ExternalCandidateContent(
                candidate.HitId,
                candidate.Title,
                candidate.ShortSummary)).ToArray(),
            maximumCharacters);

    private static BudgetedPayload BuildBudgetedPayload(
        IReadOnlyList<ExternalCandidateContent> candidates,
        int maximumCharacters)
    {
        var accepted = new List<ExternalCandidateContent>(candidates.Count);
        var truncated = false;
        foreach (var candidate in candidates)
        {
            if (Fits(accepted, candidate, maximumCharacters))
            {
                accepted.Add(candidate);
                continue;
            }

            var shortened = TryCreateTruncatedCandidate(accepted, candidate, maximumCharacters);
            if (shortened is not null)
            {
                accepted.Add(shortened);
                truncated = true;
            }

            break;
        }

        return new BudgetedPayload(
            accepted,
            SerializeCanonical(accepted),
            truncated);
    }

    private static ExternalCandidateContent? TryCreateTruncatedCandidate(
        IReadOnlyList<ExternalCandidateContent> accepted,
        ExternalCandidateContent source,
        int maximumCharacters)
    {
        if (source.ShortSummary.Length > 0)
        {
            var summaryCandidate = FindLongestPrefix(
                source.ShortSummary,
                prefix => source with { ShortSummary = prefix + TruncationMarker },
                accepted,
                maximumCharacters);
            if (summaryCandidate is not null)
            {
                return summaryCandidate;
            }
        }

        var replacementSummary = source.ShortSummary.Length == 0 ? string.Empty : TruncationMarker;
        if (source.Title.Length > 0)
        {
            var titleCandidate = FindLongestPrefix(
                source.Title,
                prefix => source with
                {
                    Title = prefix + TruncationMarker,
                    ShortSummary = replacementSummary,
                },
                accepted,
                maximumCharacters);
            if (titleCandidate is not null)
            {
                return titleCandidate;
            }
        }

        var markerOnly = source with { Title = TruncationMarker, ShortSummary = string.Empty };
        return Fits(accepted, markerOnly, maximumCharacters) ? markerOnly : null;
    }

    private static ExternalCandidateContent? FindLongestPrefix(
        string source,
        Func<string, ExternalCandidateContent> createCandidate,
        IReadOnlyList<ExternalCandidateContent> accepted,
        int maximumCharacters)
    {
        var lower = 0;
        var upper = source.Length;
        ExternalCandidateContent? best = null;
        while (lower <= upper)
        {
            var length = lower + ((upper - lower) / 2);
            var candidate = createCandidate(source[..length]);
            if (Fits(accepted, candidate, maximumCharacters))
            {
                best = candidate;
                lower = length + 1;
            }
            else
            {
                upper = length - 1;
            }
        }

        return best;
    }

    private static bool Fits(
        IReadOnlyList<ExternalCandidateContent> accepted,
        ExternalCandidateContent candidate,
        int maximumCharacters)
    {
        var pending = new ExternalCandidateContent[accepted.Count + 1];
        for (var index = 0; index < accepted.Count; index++)
        {
            pending[index] = accepted[index];
        }

        pending[^1] = candidate;
        return SerializeCanonical(pending).Length <= maximumCharacters;
    }

    private static string SerializeCanonical(IReadOnlyList<ExternalCandidateContent> items) =>
        JsonSerializer.Serialize(
            items.Select(static item => new
            {
                id = item.Id,
                title = item.Title,
                shortSummary = item.ShortSummary,
            }),
            ToolJson.Options);

    private DateTimeOffset GetStartOfLocalDateUtc(DateOnly localDate)
    {
        var localDateTime = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = _timeProvider.LocalTimeZone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset).ToUniversalTime();
    }

    private static int CountEligibleDocuments(
        IEnumerable<WebDocument> documents,
        DateTimeOffset windowStartUtc,
        DateTimeOffset nowUtc) =>
        documents.Count(document => IsEligibleForWindow(document, windowStartUtc, nowUtc));

    private static bool IsEligibleForWindow(
        WebDocument document,
        DateTimeOffset windowStartUtc,
        DateTimeOffset nowUtc) =>
        document.DateStatus == PublicationDateStatus.Verified &&
        document.PublicationTime is { } publicationTime &&
        publicationTime >= windowStartUtc &&
        publicationTime <= nowUtc + FutureDateTolerance;

    private static List<WebDocument> SortAndPrioritize(
        IEnumerable<WebDocument> documents,
        IReadOnlyList<string> preferredDomains) =>
        documents
            .OrderByDescending(document => preferredDomains.Any(domain =>
                string.Equals(document.Domain, domain, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(document => document.DateStatus == PublicationDateStatus.Verified)
            .ThenByDescending(document => document.PublicationTime)
            .ThenBy(document => document.CanonicalUrl, StringComparer.Ordinal)
            .ToList();

    private static string? SummarizeResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidateCount", out var count) ||
            !root.TryGetProperty("retrievalsUsed", out var retrievals))
        {
            return null;
        }

        var insufficient = root.TryGetProperty("insufficient", out var insuff) && insuff.GetBoolean();
        if (root.TryGetProperty("searchStatus", out var status) &&
            !string.Equals(status.GetString(), "available", StringComparison.Ordinal))
        {
            var failures = root.TryGetProperty("searchFailureCount", out var failureCount)
                ? failureCount.GetInt32()
                : 0;
            var httpStatus = root.TryGetProperty("searchErrorStatusCode", out var errorStatus) &&
                             errorStatus.ValueKind == JsonValueKind.Number
                ? $" (HTTP {errorStatus.GetInt32()})"
                : string.Empty;
            return $"Web search status is {status.GetString()}{httpStatus} after {failures} failed query attempt(s); source availability could not be determined.";
        }

        return $"Discovered {count.GetInt32()} protected candidate(s) using {retrievals.GetInt32()} retrieval attempt(s){(insufficient ? " (insufficient)" : string.Empty)}.";
    }

    private static IReadOnlyList<Theme> ParseThemes(string themesJson)
    {
        if (string.IsNullOrWhiteSpace(themesJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(themesJson);
            var themes = new List<Theme>();
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return themes;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var terms = ParseStringArray(element, "terms");
                if (terms.Count == 0)
                {
                    continue;
                }

                var includeTerms = ParseStringArray(element, "includeTerms");
                var excludeTerms = ParseStringArray(element, "excludeTerms");
                themes.Add(new Theme(
                    terms,
                    includeTerms.Count > 0 ? includeTerms : null,
                    excludeTerms.Count > 0 ? excludeTerms : null));
            }

            return themes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseDomains(string domainsJson)
    {
        if (string.IsNullOrWhiteSpace(domainsJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(domainsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()?.Trim().ToLowerInvariant())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static FreshnessWindow ParseFreshnessWindow(string value) =>
        string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
            ? FreshnessWindow.None
            : FreshnessWindow.Sliding;

    private static bool TryGetSupportedAbsoluteUrl(string? value, out string url)
    {
        url = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    private static string? ExtractTitle(string html, string? mediaType)
    {
        if (!IsHtml(mediaType))
        {
            return null;
        }

        var title = LoadHtml(html).DocumentNode.SelectSingleNode("//title")?.InnerText;
        return string.IsNullOrWhiteSpace(title) ? null : WebUtility.HtmlDecode(title).Trim();
    }

    private static DateTimeOffset? ExtractPublicationDate(string html, string? mediaType)
    {
        if (!IsHtml(mediaType))
        {
            return null;
        }

        var document = LoadHtml(html);
        var metadataNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "article:published_time", "date", "publish_date", "pubdate",
            "dc.date", "datepublished", "parsely-pub-date",
        };
        foreach (var meta in document.DocumentNode.SelectNodes("//meta[@content]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            var property = meta.GetAttributeValue("property", string.Empty);
            var name = meta.GetAttributeValue("name", string.Empty);
            var itemProperty = meta.GetAttributeValue("itemprop", string.Empty);
            if ((metadataNames.Contains(property) ||
                 metadataNames.Contains(name) ||
                 metadataNames.Contains(itemProperty)) &&
                TryParsePublicationDate(meta.GetAttributeValue("content", string.Empty), out var date))
            {
                return date;
            }
        }

        foreach (var script in document.DocumentNode.SelectNodes("//script[@type]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            if (!string.Equals(
                    script.GetAttributeValue("type", string.Empty).Trim(),
                    "application/ld+json",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(WebUtility.HtmlDecode(script.InnerText));
                if (TryFindJsonDatePublished(json.RootElement, out var jsonDate))
                {
                    return jsonDate;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed optional metadata.
            }
        }

        foreach (var time in document.DocumentNode.SelectNodes("//time[@datetime]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            if (TryParsePublicationDate(time.GetAttributeValue("datetime", string.Empty), out var date))
            {
                return date;
            }
        }

        return null;
    }

    private static bool TryFindJsonDatePublished(JsonElement element, out DateTimeOffset date)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "datePublished", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    TryParsePublicationDate(property.Value.GetString(), out date))
                {
                    return true;
                }

                if (TryFindJsonDatePublished(property.Value, out date))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindJsonDatePublished(item, out date))
                {
                    return true;
                }
            }
        }

        date = default;
        return false;
    }

    private static bool TryParsePublicationDate(string? value, out DateTimeOffset date) =>
        DateTimeOffset.TryParse(
            WebUtility.HtmlDecode(value ?? string.Empty),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out date);

    private static WebDocumentKind ClassifyDocumentKind(string url, string? mediaType)
    {
        if (mediaType is not null && mediaType.Contains("video", StringComparison.OrdinalIgnoreCase))
        {
            return WebDocumentKind.Video;
        }

        var lowerUrl = url.ToLowerInvariant();
        if (lowerUrl.Contains("github.com") || lowerUrl.Contains("gitlab.com"))
        {
            return WebDocumentKind.Repository;
        }

        if (lowerUrl.Contains("docs.") || lowerUrl.Contains("documentation") ||
            lowerUrl.Contains("/reference/") || lowerUrl.Contains("/api/"))
        {
            return WebDocumentKind.Documentation;
        }

        if (lowerUrl.Contains("release") || lowerUrl.Contains("changelog") || lowerUrl.Contains("breaking"))
        {
            return WebDocumentKind.ReleaseNote;
        }

        if (lowerUrl.Contains("blog") || lowerUrl.Contains("news") || lowerUrl.Contains("article"))
        {
            return WebDocumentKind.Article;
        }

        if (lowerUrl.Contains("stackoverflow.com") || lowerUrl.Contains("reddit.com") || lowerUrl.Contains("forum"))
        {
            return WebDocumentKind.Post;
        }

        return WebDocumentKind.Other;
    }

    private static string ExtractSummary(string html, string? mediaType)
    {
        if (!IsHtml(mediaType))
        {
            return string.Empty;
        }

        var document = LoadHtml(html);
        foreach (var meta in document.DocumentNode.SelectNodes("//meta[@content]") ?? Enumerable.Empty<HtmlAgilityPack.HtmlNode>())
        {
            var name = meta.GetAttributeValue("name", string.Empty);
            var property = meta.GetAttributeValue("property", string.Empty);
            if (string.Equals(name, "description", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property, "og:description", StringComparison.OrdinalIgnoreCase))
            {
                var content = WebUtility.HtmlDecode(meta.GetAttributeValue("content", string.Empty)).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return TruncateSummary(content);
                }
            }
        }

        var paragraph = document.DocumentNode.SelectSingleNode("//article//p|//main//p|//body//p|//p");
        var text = paragraph is null ? string.Empty : WebUtility.HtmlDecode(paragraph.InnerText).Trim();
        return TruncateSummary(text);
    }

    private static HtmlAgilityPack.HtmlDocument LoadHtml(string html)
    {
        var document = new HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(html);
        return document;
    }

    private static bool IsHtml(string? mediaType) =>
        mediaType is null || mediaType.Contains("html", StringComparison.OrdinalIgnoreCase);

    private static string TruncateSummary(string text)
    {
        const int maximumLength = 500;
        return text.Length <= maximumLength
            ? text
            : text[..maximumLength] + TruncationMarker;
    }

    private sealed record QuerySearchResult(
        CompiledQuery CompiledQuery,
        IReadOnlyList<WebSearchResultItem> SearchResults,
        bool Succeeded = true,
        int? ErrorStatusCode = null);

    private sealed record ExternalCandidateContent(string Id, string Title, string ShortSummary);

    private sealed record BudgetedPayload(
        IReadOnlyList<ExternalCandidateContent> Items,
        string CanonicalPayload,
        bool Truncated);

    private sealed class DiscoveryCollectionState
    {
        public Dictionary<string, WebDocument> Documents { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> AttemptedUrls { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<object> GeneratedQueries { get; } = [];

        public List<object> WindowHistory { get; } = [];

        public int SuccessfulSearchQueryCount { get; set; }

        public int SearchFailureCount { get; set; }

        public int? LastSearchErrorStatusCode { get; set; }

        public bool SearchRateLimited { get; set; }

        public bool SearchStopped { get; set; }

        public int RetrievalsUsed { get; set; }
    }
}
