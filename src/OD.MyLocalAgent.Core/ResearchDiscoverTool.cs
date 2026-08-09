using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Discovers web documents matching search themes using bounded loops.
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

    private readonly IWebSearchConnector _webSearchConnector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<HttpOptions> _httpOptions;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IContentTrustService _contentTrustService;
    private readonly IIncidentReporter _incidentReporter;
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
        ILogger<ResearchDiscoverTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(webSearchConnector);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(httpOptions);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(contentTrustService);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _webSearchConnector = webSearchConnector;
        _httpClientFactory = httpClientFactory;
        _httpOptions = httpOptions;
        _guardrailsOptions = guardrailsOptions;
        _contentTrustService = contentTrustService;
        _incidentReporter = incidentReporter;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Tool("research.discover", "Discovers web documents matching search themes with bounded search and retrieval.", 100)]
    public Task<string> DiscoverAsync(
        [ToolArgument(WorkflowArgumentKind.String, "JSON array of search themes.", Example = "[{\"terms\":[\".NET\",\"C#\"],\"includeTerms\":[\"release\"]}]")] string themes,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Freshness window mode: 'sliding' for 2→4→7 day expansion, 'none' for no date filter.", AllowedValues = ["sliding", "none"], Example = "sliding")] string freshnessWindow = "sliding",
        [ToolArgument(WorkflowArgumentKind.String, "BCP-47 source language tag for search filtering.", Example = "en")] string sourceLanguage = "en",
        [ToolArgument(WorkflowArgumentKind.String, "BCP-47 target language for translation, or empty for original language.", Example = "fr")] string language = "",
        [ToolArgument(WorkflowArgumentKind.String, "JSON array of preferred domains for ranking.", Example = "[\"learn.microsoft.com\"]")] string preferredDomains = "[]",
        [ToolArgument(WorkflowArgumentKind.Integer, "Minimum unique dated documents target (1-100), ignored in 'none' mode.", Example = "8")] int minimumDocuments = 8,
        [ToolArgument(WorkflowArgumentKind.Integer, "Maximum search hits per compiled query (1-10).", Example = "5")] int maxResultsPerQuery = 5,
        [ToolArgument(WorkflowArgumentKind.Integer, "Maximum page fetches for the entire run (1-100).", Example = "24")] int maxRetrievalsPerRun = 24,
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
                var parsedDomains = ParseDomains(preferredDomains);
                var mode = ParseFreshnessWindow(freshnessWindow);
                var effectiveMaxPerQuery = Math.Clamp(maxResultsPerQuery, 1, 10);
                var effectiveMaxRetrievals = Math.Clamp(maxRetrievalsPerRun, 1, 100);
                var effectiveMinDocs = Math.Clamp(minimumDocuments, 1, 100);

                var effectiveLanguage = string.IsNullOrWhiteSpace(language) ? null : language;
                var effectiveSourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "en" : sourceLanguage;

                return mode == FreshnessWindow.Sliding
                    ? await RunSlidingModeAsync(
                        parsedThemes,
                        effectiveSourceLanguage,
                        effectiveLanguage,
                        parsedDomains,
                        effectiveMinDocs,
                        effectiveMaxPerQuery,
                        effectiveMaxRetrievals,
                        cancellationToken).ConfigureAwait(false)
                    : await RunNoneModeAsync(
                        parsedThemes,
                        effectiveSourceLanguage,
                        effectiveLanguage,
                        parsedDomains,
                        effectiveMaxPerQuery,
                        effectiveMaxRetrievals,
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
        CancellationToken cancellationToken)
    {
        var allCandidates = new Dictionary<string, WebDocument>(StringComparer.OrdinalIgnoreCase);
        var generatedQueries = new List<object>();
        var retrievalsUsed = 0;
        var queriesExecuted = 0;

        // Phase 1: Two-day window
        var budgetRemaining = Math.Min(themes.Count * 3, maxResultsPerQuery * 3);
        var twoDayQueries = QueryCompiler.Compile(
            themes,
            sourceLanguage,
            2,
            preferredDomains,
            budgetRemaining);

        var twoDayResults = await ExecuteQueriesAsync(
            twoDayQueries,
            maxResultsPerQuery,
            cancellationToken).ConfigureAwait(false);
        queriesExecuted += twoDayQueries.Count;

        foreach (var queryResult in twoDayResults)
        {
            generatedQueries.Add(new
            {
                query = queryResult.CompiledQuery.Query,
                themeIndex = queryResult.CompiledQuery.ThemeIndex,
                intent = queryResult.CompiledQuery.Intent.ToString(),
                resultCount = queryResult.SearchResults.Count,
            });

            foreach (var hit in queryResult.SearchResults)
            {
                var canonicalUrl = UrlCanonicalizer.Canonicalize(hit.Url);
                if (allCandidates.ContainsKey(canonicalUrl))
                {
                    continue;
                }

                if (retrievalsUsed >= maxRetrievalsPerRun)
                {
                    break;
                }

                var document = await RetrieveAndScreenAsync(
                    hit,
                    canonicalUrl,
                    queryResult.CompiledQuery.Query,
                    cancellationToken).ConfigureAwait(false);

                if (document is not null)
                {
                    allCandidates[canonicalUrl] = document;
                    retrievalsUsed++;
                }
            }

            if (retrievalsUsed >= maxRetrievalsPerRun)
            {
                break;
            }
        }

        var datedCount = allCandidates.Values.Count(
            d => d.DateStatus == PublicationDateStatus.Verified &&
                 d.PublicationTime.HasValue);

        // Phase 2: Four-day window if below threshold
        if (datedCount < minimumDocuments && retrievalsUsed < maxRetrievalsPerRun)
        {
            var fourDayQueries = QueryCompiler.Compile(
                themes,
                sourceLanguage,
                4,
                preferredDomains,
                budgetRemaining);

            var fourDayResults = await ExecuteQueriesAsync(
                fourDayQueries,
                maxResultsPerQuery,
                cancellationToken).ConfigureAwait(false);
            queriesExecuted += fourDayQueries.Count;

            foreach (var queryResult in fourDayResults)
            {
                generatedQueries.Add(new
                {
                    query = queryResult.CompiledQuery.Query,
                    themeIndex = queryResult.CompiledQuery.ThemeIndex,
                    intent = queryResult.CompiledQuery.Intent.ToString(),
                    resultCount = queryResult.SearchResults.Count,
                });

                foreach (var hit in queryResult.SearchResults)
                {
                    var canonicalUrl = UrlCanonicalizer.Canonicalize(hit.Url);
                    if (allCandidates.ContainsKey(canonicalUrl))
                    {
                        continue;
                    }

                    if (retrievalsUsed >= maxRetrievalsPerRun)
                    {
                        break;
                    }

                    var document = await RetrieveAndScreenAsync(
                        hit,
                        canonicalUrl,
                        queryResult.CompiledQuery.Query,
                        cancellationToken).ConfigureAwait(false);

                    if (document is not null)
                    {
                        allCandidates[canonicalUrl] = document;
                        retrievalsUsed++;
                    }
                }

                if (retrievalsUsed >= maxRetrievalsPerRun)
                {
                    break;
                }
            }

            datedCount = allCandidates.Values.Count(
                d => d.DateStatus == PublicationDateStatus.Verified &&
                     d.PublicationTime.HasValue);
        }

        // Phase 3: Seven-day window if still below threshold
        if (datedCount < minimumDocuments && retrievalsUsed < maxRetrievalsPerRun)
        {
            var sevenDayQueries = QueryCompiler.Compile(
                themes,
                sourceLanguage,
                7,
                preferredDomains,
                budgetRemaining);

            var sevenDayResults = await ExecuteQueriesAsync(
                sevenDayQueries,
                maxResultsPerQuery,
                cancellationToken).ConfigureAwait(false);
            queriesExecuted += sevenDayQueries.Count;

            foreach (var queryResult in sevenDayResults)
            {
                generatedQueries.Add(new
                {
                    query = queryResult.CompiledQuery.Query,
                    themeIndex = queryResult.CompiledQuery.ThemeIndex,
                    intent = queryResult.CompiledQuery.Intent.ToString(),
                    resultCount = queryResult.SearchResults.Count,
                });

                foreach (var hit in queryResult.SearchResults)
                {
                    var canonicalUrl = UrlCanonicalizer.Canonicalize(hit.Url);
                    if (allCandidates.ContainsKey(canonicalUrl))
                    {
                        continue;
                    }

                    if (retrievalsUsed >= maxRetrievalsPerRun)
                    {
                        break;
                    }

                    var document = await RetrieveAndScreenAsync(
                        hit,
                        canonicalUrl,
                        queryResult.CompiledQuery.Query,
                        cancellationToken).ConfigureAwait(false);

                    if (document is not null)
                    {
                        allCandidates[canonicalUrl] = document;
                        retrievalsUsed++;
                    }
                }

                if (retrievalsUsed >= maxRetrievalsPerRun)
                {
                    break;
                }
            }

            datedCount = allCandidates.Values.Count(
                d => d.DateStatus == PublicationDateStatus.Verified &&
                     d.PublicationTime.HasValue);
        }

        var insufficient = datedCount < minimumDocuments;
        var candidates = SortAndPrioritize(allCandidates.Values, preferredDomains);

        // Translate if needed
        IReadOnlyList<WebDocument> finalCandidates = candidates;
        if (!string.IsNullOrEmpty(targetLanguage))
        {
            finalCandidates = await TranslateCandidatesAsync(
                candidates,
                targetLanguage,
                cancellationToken).ConfigureAwait(false);
        }

        return BuildResult(
            "sliding",
            generatedQueries,
            finalCandidates,
            retrievalsUsed,
            insufficient);
    }

    private async Task<string> RunNoneModeAsync(
        IReadOnlyList<Theme> themes,
        string sourceLanguage,
        string? targetLanguage,
        IReadOnlyList<string> preferredDomains,
        int maxResultsPerQuery,
        int maxRetrievalsPerRun,
        CancellationToken cancellationToken)
    {
        var allCandidates = new Dictionary<string, WebDocument>(StringComparer.OrdinalIgnoreCase);
        var generatedQueries = new List<object>();
        var retrievalsUsed = 0;

        var budgetRemaining = Math.Min(themes.Count * 3, maxResultsPerQuery * 3);
        var queries = QueryCompiler.Compile(
            themes,
            sourceLanguage,
            0,
            preferredDomains,
            budgetRemaining);

        var queryResults = await ExecuteQueriesAsync(
            queries,
            maxResultsPerQuery,
            cancellationToken).ConfigureAwait(false);

        foreach (var queryResult in queryResults)
        {
            generatedQueries.Add(new
            {
                query = queryResult.CompiledQuery.Query,
                themeIndex = queryResult.CompiledQuery.ThemeIndex,
                intent = queryResult.CompiledQuery.Intent.ToString(),
                resultCount = queryResult.SearchResults.Count,
            });

            foreach (var hit in queryResult.SearchResults)
            {
                var canonicalUrl = UrlCanonicalizer.Canonicalize(hit.Url);
                if (allCandidates.ContainsKey(canonicalUrl))
                {
                    continue;
                }

                if (retrievalsUsed >= maxRetrievalsPerRun)
                {
                    break;
                }

                var document = await RetrieveAndScreenAsync(
                    hit,
                    canonicalUrl,
                    queryResult.CompiledQuery.Query,
                    cancellationToken).ConfigureAwait(false);

                if (document is not null)
                {
                    allCandidates[canonicalUrl] = document;
                    retrievalsUsed++;
                }
            }

            if (retrievalsUsed >= maxRetrievalsPerRun)
            {
                break;
            }
        }

        var candidates = SortAndPrioritize(allCandidates.Values, preferredDomains);

        IReadOnlyList<WebDocument> finalCandidates = candidates;
        if (!string.IsNullOrEmpty(targetLanguage))
        {
            finalCandidates = await TranslateCandidatesAsync(
                candidates,
                targetLanguage,
                cancellationToken).ConfigureAwait(false);
        }

        return BuildResult(
            "none",
            generatedQueries,
            finalCandidates,
            retrievalsUsed,
            insufficient: false);
    }

    private async Task<IReadOnlyList<QuerySearchResult>> ExecuteQueriesAsync(
        IReadOnlyList<CompiledQuery> queries,
        int maxResultsPerQuery,
        CancellationToken cancellationToken)
    {
        var results = new List<QuerySearchResult>();

        foreach (var query in queries)
        {
            try
            {
                var searchResult = await _webSearchConnector
                    .SearchAsync(query.Query, maxResultsPerQuery, cancellationToken)
                    .ConfigureAwait(false);

                results.Add(new QuerySearchResult(query, searchResult.Items));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    "Query execution failed for '{Query}': {ExceptionType}",
                    query.Query,
                    exception.GetType().Name);
                results.Add(new QuerySearchResult(query, []));
            }
        }

        return results;
    }

    private async Task<WebDocument?> RetrieveAndScreenAsync(
        WebSearchResultItem hit,
        string canonicalUrl,
        string queryId,
        CancellationToken cancellationToken)
    {
        try
        {
            var domain = UrlCanonicalizer.ExtractDomain(hit.Url);
            var document = await FetchDocumentAsync(
                hit,
                canonicalUrl,
                domain,
                queryId,
                cancellationToken).ConfigureAwait(false);

            if (document is null)
            {
                return null;
            }

            // Apply Guardrails screening to the document content
            var screeningResult = await ScreenWithFallbackAsync(
                document.ShortSummary,
                cancellationToken).ConfigureAwait(false);

            return document with
            {
                TrustScore = screeningResult.TrustScore ?? 0,
                TrustDisposition = ContentTrustPolicy.Decide(
                    screeningResult,
                    _guardrailsOptions.CurrentValue).ToString(),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Document retrieval failed for '{Url}': {ExceptionType}",
                hit.Url,
                exception.GetType().Name);
            return null;
        }
    }

    private async Task<WebDocument?> FetchDocumentAsync(
        WebSearchResultItem hit,
        string canonicalUrl,
        string domain,
        string queryId,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(
            TimeSpan.FromSeconds(_httpOptions.CurrentValue.TimeoutSeconds),
            _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpTool.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, hit.Url);
            if (!string.IsNullOrWhiteSpace(_httpOptions.CurrentValue.UserAgent))
            {
                request.Headers.UserAgent.ParseAdd(_httpOptions.CurrentValue.UserAgent);
            }

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linkedSource.Token).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(linkedSource.Token)
                .ConfigureAwait(false);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var title = ExtractTitle(body, mediaType) ?? hit.Title;
            var publicationTime = ExtractPublicationDate(body, mediaType);
            var dateStatus = publicationTime.HasValue
                ? PublicationDateStatus.Verified
                : PublicationDateStatus.Unknown;
            var kind = ClassifyDocumentKind(hit.Url, body, mediaType);
            var shortSummary = ExtractSummary(body, mediaType);
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? hit.Url;
            var canonicalFinalUrl = UrlCanonicalizer.Canonicalize(finalUrl);

            return new WebDocument(
                HitId: Guid.NewGuid().ToString("N"),
                SourceUrl: hit.Url,
                FinalUrl: finalUrl,
                CanonicalUrl: canonicalUrl,
                Title: title,
                Domain: domain,
                Kind: kind,
                PublicationTime: publicationTime,
                DateStatus: dateStatus,
                ShortSummary: shortSummary,
                ContentType: mediaType ?? "text/html",
                TrustScore: 0,
                TrustDisposition: "Pending",
                DiscoveryQueryIds: [queryId],
                RetrievalTimeUtc: _timeProvider.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ContentTrustResult> ScreenWithFallbackAsync(
        string content,
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
                .ReportAsync(
                    exception,
                    "research.discover guardrail screening",
                    CancellationToken.None)
                .ConfigureAwait(false);
            return exception.FallbackResult;
        }
    }

    private async Task<IReadOnlyList<WebDocument>> TranslateCandidatesAsync(
        IReadOnlyList<WebDocument> candidates,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        // Build a single translation prompt for all candidates
        var sb = new StringBuilder();
        for (var i = 0; i < candidates.Count; i++)
        {
            var doc = candidates[i];
            sb.AppendLine($"--- DOCUMENT {i + 1} ---");
            sb.AppendLine($"TITLE: {doc.Title}");
            sb.AppendLine($"SUMMARY: {doc.ShortSummary}");
            sb.AppendLine();
        }

        sb.Insert(0, $"Translate the titles and summaries below to {targetLanguage}. Return each document's translated title and summary in the exact same format with --- DOCUMENT N --- separators. Do not add any commentary.\n\n");

        try
        {
            // Use the existing LLM infrastructure for translation
            // This is a bounded, deterministic call
            var translated = await TranslateAsync(
                sb.ToString(),
                targetLanguage,
                cancellationToken).ConfigureAwait(false);

            return ParseTranslatedCandidates(candidates, translated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // If translation fails, return original candidates
            return candidates;
        }
    }

    private async Task<string> TranslateAsync(
        string content,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        // Use the Guardrails service for a bounded translation call
        // This uses the configured LLM provider without tools or agent
        var result = await _contentTrustService
            .SummarizeAsync(
                content,
                content.Length,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Summary ?? content;
    }

    private static IReadOnlyList<WebDocument> ParseTranslatedCandidates(
        IReadOnlyList<WebDocument> originals,
        string translated)
    {
        // If parsing fails, return originals
        var sections = translated.Split(
            "--- DOCUMENT ",
            StringSplitOptions.RemoveEmptyEntries);

        if (sections.Length < originals.Count)
        {
            return originals;
        }

        var result = new List<WebDocument>(originals.Count);
        for (var i = 0; i < originals.Count; i++)
        {
            var section = i < sections.Length ? sections[i] : string.Empty;
            var titleMatch = ExtractTranslatedField(section, "TITLE:");
            var summaryMatch = ExtractTranslatedField(section, "SUMMARY:");

            result.Add(originals[i] with
            {
                Title = titleMatch ?? originals[i].Title,
                ShortSummary = summaryMatch ?? originals[i].ShortSummary,
            });
        }

        return result;
    }

    private static string? ExtractTranslatedField(string section, string prefix)
    {
        var prefixIndex = section.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex < 0)
        {
            return null;
        }

        var start = prefixIndex + prefix.Length;
        var lineEnd = section.IndexOf('\n', start);
        if (lineEnd < 0)
        {
            lineEnd = section.Length;
        }

        var value = section[start..lineEnd].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static List<WebDocument> SortAndPrioritize(
        IEnumerable<WebDocument> documents,
        IReadOnlyList<string> preferredDomains)
    {
        return documents
            .OrderByDescending(d => preferredDomains.Any(
                p => string.Equals(d.Domain, p, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(d => d.DateStatus == PublicationDateStatus.Verified)
            .ThenByDescending(d => d.PublicationTime)
            .ThenBy(d => d.DateStatus == PublicationDateStatus.Unknown ? 1 : 0)
            .ToList();
    }

    private static string BuildResult(
        string mode,
        IReadOnlyList<object> queries,
        IReadOnlyList<WebDocument> candidates,
        int retrievalsUsed,
        bool insufficient)
    {
        var output = new Dictionary<string, object?>
        {
            ["freshnessWindow"] = mode,
            ["queries"] = queries,
            ["queryCount"] = queries.Count,
            ["retrievalsUsed"] = retrievalsUsed,
            ["candidateCount"] = candidates.Count,
            ["datedCount"] = candidates.Count(
                d => d.DateStatus == PublicationDateStatus.Verified),
            ["unknownDateCount"] = candidates.Count(
                d => d.DateStatus == PublicationDateStatus.Unknown),
            ["candidates"] = candidates.Select(c => new
            {
                hitId = c.HitId,
                sourceUrl = c.SourceUrl,
                finalUrl = c.FinalUrl,
                canonicalUrl = c.CanonicalUrl,
                title = c.Title,
                domain = c.Domain,
                kind = c.Kind.ToString(),
                publicationTime = c.PublicationTime?.ToString("O"),
                dateStatus = c.DateStatus.ToString(),
                shortSummary = c.ShortSummary,
                contentType = c.ContentType,
                trustScore = c.TrustScore,
                trustDisposition = c.TrustDisposition,
                discoveryQueryIds = c.DiscoveryQueryIds,
                retrievalTimeUtc = c.RetrievalTimeUtc.ToString("O"),
            }),
        };

        if (insufficient)
        {
            output["insufficient"] = true;
            output["insufficientReason"] =
                $"Only {candidates.Count(d => d.DateStatus == PublicationDateStatus.Verified)} dated documents found after 7-day expansion, below the minimum threshold.";
        }

        return ToolJson.Serialize(output);
    }

    private static string? SummarizeResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("candidateCount", out var count) ||
            !root.TryGetProperty("retrievalsUsed", out var retrievals))
        {
            return null;
        }

        var insufficient = root.TryGetProperty("insufficient", out var insuff) &&
                           insuff.GetBoolean();
        var suffix = insufficient ? " (insufficient)" : string.Empty;

        return $"Discovered {count.GetInt32()} candidate(s) using {retrievals.GetInt32()} retrieval(s){suffix}.";
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

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
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
            }

            return themes;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseStringArray(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value);
                }
            }
        }

        return result;
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
            var domains = new List<string>();

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var value = element.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            domains.Add(value);
                        }
                    }
                }
            }

            return domains;
        }
        catch
        {
            return [];
        }
    }

    private static FreshnessWindow ParseFreshnessWindow(string value)
    {
        return string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
            ? FreshnessWindow.None
            : FreshnessWindow.Sliding;
    }

    private static string? ExtractTitle(string html, string? mediaType)
    {
        if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var titleTag = "<title";
        var titleIndex = html.IndexOf(titleTag, StringComparison.OrdinalIgnoreCase);
        if (titleIndex < 0)
        {
            return null;
        }

        var start = html.IndexOf('>', titleIndex);
        if (start < 0)
        {
            return null;
        }

        var end = html.IndexOf("</title", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            end = Math.Min(start + 500, html.Length);
        }

        return html[(start + 1)..end].Trim();
    }

    private static DateTimeOffset? ExtractPublicationDate(string html, string? mediaType)
    {
        if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Look for common date patterns in meta tags
        var datePatterns = new[]
        {
            "<meta property=\"article:published_time\" content=\"",
            "<meta name=\"date\" content=\"",
            "<meta name=\"publish_date\" content=\"",
            "<meta name=\"DC.date\" content=\"",
        };

        foreach (var pattern in datePatterns)
        {
            var index = html.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = index + pattern.Length;
                var end = html.IndexOf('"', start);
                if (end > start)
                {
                    var dateStr = html[start..end];
                    if (DateTimeOffset.TryParse(dateStr, out var date))
                    {
                        return date;
                    }
                }
            }
        }

        return null;
    }

    private static WebDocumentKind ClassifyDocumentKind(
        string url,
        string html,
        string? mediaType)
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

        if (lowerUrl.Contains("release") || lowerUrl.Contains("changelog") ||
            lowerUrl.Contains("breaking"))
        {
            return WebDocumentKind.ReleaseNote;
        }

        if (lowerUrl.Contains("blog") || lowerUrl.Contains("news") ||
            lowerUrl.Contains("article"))
        {
            return WebDocumentKind.Article;
        }

        if (lowerUrl.Contains("stackoverflow.com") || lowerUrl.Contains("reddit.com") ||
            lowerUrl.Contains("forum"))
        {
            return WebDocumentKind.Post;
        }

        return WebDocumentKind.Other;
    }

    private static string ExtractSummary(string html, string? mediaType)
    {
        if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Try meta description first
        var metaDesc = ExtractMetaContent(html, "description");
        if (!string.IsNullOrWhiteSpace(metaDesc))
        {
            return TruncateSummary(metaDesc);
        }

        // Fall back to first paragraph
        var firstP = ExtractFirstParagraph(html);
        if (!string.IsNullOrWhiteSpace(firstP))
        {
            return TruncateSummary(firstP);
        }

        return string.Empty;
    }

    private static string? ExtractMetaContent(string html, string name)
    {
        var patterns = new[]
        {
            $"<meta name=\"{name}\" content=\"",
            $"<meta property=\"og:{name}\" content=\"",
        };

        foreach (var pattern in patterns)
        {
            var index = html.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var start = index + pattern.Length;
                var end = html.IndexOf('"', start);
                if (end > start)
                {
                    return html[start..end].Trim();
                }
            }
        }

        return null;
    }

    private static string ExtractFirstParagraph(string html)
    {
        var pIndex = html.IndexOf("<p", StringComparison.OrdinalIgnoreCase);
        if (pIndex < 0)
        {
            return string.Empty;
        }

        var start = html.IndexOf('>', pIndex);
        if (start < 0)
        {
            return string.Empty;
        }

        var end = html.IndexOf("</p", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            end = Math.Min(start + 1000, html.Length);
        }

        // Strip HTML tags from the paragraph content
        var content = html[(start + 1)..end];
        return StripHtmlTags(content).Trim();
    }

    private static string StripHtmlTags(string html)
    {
        var result = new StringBuilder();
        var inTag = false;

        foreach (var c in html)
        {
            if (c == '<')
            {
                inTag = true;
                continue;
            }

            if (c == '>')
            {
                inTag = false;
                continue;
            }

            if (!inTag)
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    private static string TruncateSummary(string text)
    {
        const int maxLength = 500;
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + TruncationMarker;
    }

    private sealed record QuerySearchResult(
        CompiledQuery CompiledQuery,
        IReadOnlyList<WebSearchResultItem> SearchResults);
}
