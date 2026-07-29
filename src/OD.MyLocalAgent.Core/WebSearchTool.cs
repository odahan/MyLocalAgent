using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Searches the selected provider and protects the exact delivered result payload.
/// </summary>
public sealed class WebSearchTool : IWebSearchTool
{
    private const string TruncationMarker = "[truncated by OD.MyLocalAgent]";

    private readonly IWebSearchConnector _connector;
    private readonly IOptionsMonitor<WebSearchOptions> _webSearchOptions;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IContentTrustService _contentTrustService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly ILogger<WebSearchTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSearchTool"/> class.
    /// </summary>
    /// <param name="connector">The provider-independent web-search connector.</param>
    /// <param name="webSearchOptions">The reloadable web-search options.</param>
    /// <param name="guardrailsOptions">The reloadable Guardrails policy.</param>
    /// <param name="contentTrustService">The mandatory content trust service.</param>
    /// <param name="incidentReporter">The single incident-reporting entry point.</param>
    /// <param name="logger">The structured logger.</param>
    /// <param name="timeProvider">The time source used for tool-call duration.</param>
    public WebSearchTool(
        IWebSearchConnector connector,
        IOptionsMonitor<WebSearchOptions> webSearchOptions,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IContentTrustService contentTrustService,
        IIncidentReporter incidentReporter,
        ILogger<WebSearchTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(webSearchOptions);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(contentTrustService);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connector = connector;
        _webSearchOptions = webSearchOptions;
        _guardrailsOptions = guardrailsOptions;
        _contentTrustService = contentTrustService;
        _incidentReporter = incidentReporter;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Description("Searches the web through the configured cloud provider and returns one Guardrails-protected compact result set.")]
    public Task<string> SearchAsync(
        [Description("Search query sent to the configured cloud provider.")] string query,
        [Description("Optional result limit; defaults to WebSearch:DefaultMaxResults.")] int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var options = _webSearchOptions.CurrentValue;
        var effectiveMaxResults = Math.Clamp(
            maxResults ?? options.DefaultMaxResults,
            1,
            options.MaxResultsCap);
        var guardrails = _guardrailsOptions.CurrentValue;
        if (guardrails.MaxContentChars < 2)
        {
            throw new InvalidOperationException(
                "Guardrails:MaxContentChars must be at least 2 for Web Search.");
        }

        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "web.search",
            new
            {
                queryLength = query.Length,
                maxResults = effectiveMaxResults,
            },
            async () =>
            {
                var response = await _connector
                    .SearchAsync(query, effectiveMaxResults, cancellationToken)
                    .ConfigureAwait(false);
                var budgeted = BuildBudgetedPayload(
                    response.Items,
                    guardrails.MaxContentChars);
                var trust = await ScreenWithFallbackAsync(
                        budgeted.CanonicalPayload,
                        cancellationToken)
                    .ConfigureAwait(false);
                var disposition = ContentTrustPolicy.Decide(trust, guardrails);
                var delivery = GuardrailDelivery.Apply(
                    budgeted.CanonicalPayload,
                    trust,
                    guardrails);
                var deliverExternalData =
                    disposition != TrustDisposition.Withhold;
                var deliveredSources = deliverExternalData
                    ? budgeted.Items.Select(
                        static item => new
                        {
                            item.Index,
                            item.Url,
                        })
                    : [];
                var deliveredCount = deliverExternalData
                    ? budgeted.Items.Count
                    : 0;
                var withheld = disposition == TrustDisposition.Withhold
                    ? $"score {trust.TrustScore}"
                    : null;
                var output = new Dictionary<string, object?>
                {
                    ["provider"] = response.Provider,
                    ["query"] = query,
                    ["providerCount"] = response.Items.Count,
                    ["count"] = deliveredCount,
                    ["omittedBecauseOfBudget"] =
                        budgeted.OmittedBecauseOfBudget,
                    ["truncated"] = budgeted.Truncated,
                    ["sources"] = deliveredSources,
                    ["protectedResults"] = delivery.Content,
                    ["guardrails"] = new
                    {
                        screened = trust.Screened,
                        trustStatus = trust.Status.ToString(),
                        trustScore = trust.TrustScore,
                        suspiciousIndicatorCount =
                            trust.SuspiciousIndicators.Count,
                        disposition = disposition.ToString(),
                        contentEncoding = delivery.ContentEncoding,
                    },
                };
                if (withheld is not null)
                {
                    output["withheld"] = withheld;
                }

                return ToolJson.Serialize(output);
            });
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
                    "web.search guardrail screening",
                    CancellationToken.None)
                .ConfigureAwait(false);
            return exception.FallbackResult;
        }
    }

    private static BudgetedPayload BuildBudgetedPayload(
        IReadOnlyList<WebSearchResultItem> items,
        int maximumCharacters)
    {
        var accepted = new List<ProtectedResultItem>(items.Count);
        var truncated = false;

        for (var index = 0; index < items.Count; index++)
        {
            var source = items[index];
            var complete = new ProtectedResultItem(
                index + 1,
                source.Title,
                source.Url,
                source.Content);
            if (Fits(accepted, complete, maximumCharacters))
            {
                accepted.Add(complete);
                continue;
            }

            var shortened = TryCreateTruncatedItem(
                accepted,
                complete,
                maximumCharacters);
            if (shortened is not null)
            {
                accepted.Add(shortened);
                truncated = true;
            }

            break;
        }

        var canonical = SerializeCanonical(accepted);
        return new BudgetedPayload(
            accepted,
            canonical,
            items.Count - accepted.Count,
            truncated);
    }

    private static ProtectedResultItem? TryCreateTruncatedItem(
        IReadOnlyList<ProtectedResultItem> accepted,
        ProtectedResultItem source,
        int maximumCharacters)
    {
        if (source.Content.Length > 0)
        {
            var contentCandidate = FindLongestPrefix(
                source.Content,
                prefix => source with
                {
                    Content = prefix + TruncationMarker,
                },
                accepted,
                maximumCharacters);
            if (contentCandidate is not null)
            {
                return contentCandidate;
            }
        }

        var replacementContent = source.Content.Length == 0
            ? string.Empty
            : TruncationMarker;
        if (source.Title.Length > 0)
        {
            var titleCandidate = FindLongestPrefix(
                source.Title,
                prefix => source with
                {
                    Title = prefix + TruncationMarker,
                    Content = replacementContent,
                },
                accepted,
                maximumCharacters);
            if (titleCandidate is not null)
            {
                return titleCandidate;
            }
        }

        var markerOnly = source with
        {
            Title = TruncationMarker,
            Content = string.Empty,
        };
        return Fits(accepted, markerOnly, maximumCharacters)
            ? markerOnly
            : null;
    }

    private static ProtectedResultItem? FindLongestPrefix(
        string source,
        Func<string, ProtectedResultItem> createCandidate,
        IReadOnlyList<ProtectedResultItem> accepted,
        int maximumCharacters)
    {
        var lower = 0;
        var upper = source.Length;
        ProtectedResultItem? best = null;
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
        IReadOnlyList<ProtectedResultItem> accepted,
        ProtectedResultItem candidate,
        int maximumCharacters)
    {
        var pending = new ProtectedResultItem[accepted.Count + 1];
        for (var index = 0; index < accepted.Count; index++)
        {
            pending[index] = accepted[index];
        }

        pending[^1] = candidate;
        return SerializeCanonical(pending).Length <= maximumCharacters;
    }

    private static string SerializeCanonical(
        IReadOnlyList<ProtectedResultItem> items) =>
        JsonSerializer.Serialize(
            items.Select(
                static item => new
                {
                    item.Index,
                    item.Title,
                    item.Content,
                }),
            ToolJson.Options);

    private sealed record ProtectedResultItem(
        int Index,
        string Title,
        string Url,
        string Content);

    private sealed record BudgetedPayload(
        IReadOnlyList<ProtectedResultItem> Items,
        string CanonicalPayload,
        int OmittedBecauseOfBudget,
        bool Truncated);
}
