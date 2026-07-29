using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements allowlisted compact-JSON HTTP tools with protected GET content.
/// </summary>
public sealed partial class HttpTool : IHttpTool
{
    /// <summary>
    /// Gets the managed HTTP-client name.
    /// </summary>
    public const string HttpClientName = "OD.MyLocalAgent.HttpTool";

    private const string TooLargeMarker =
        "[HTTP PAGE NOT PROCESSED BY GUARDRAILS: content exceeded the safe summarization limit.]";
    private const string LlmDisabledMarker =
        "[HTTP PAGE NOT PROCESSED BY GUARDRAILS: LLM guardrails processing is disabled for oversized content.]";
    private const string SummarizationDisabledMarker =
        "[HTTP PAGE NOT PROCESSED BY GUARDRAILS: oversized-content summarization is disabled.]";
    private const string SummarizationFailedMarker =
        "[HTTP PAGE NOT PROCESSED BY GUARDRAILS: safe summarization failed.]";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<HttpOptions> _httpOptions;
    private readonly IOptions<SafetyOptions> _safetyOptions;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IContentTrustService _contentTrustService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly ILogger<HttpTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpTool"/> class.
    /// </summary>
    public HttpTool(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpOptions> httpOptions,
        IOptions<SafetyOptions> safetyOptions,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IContentTrustService contentTrustService,
        IIncidentReporter incidentReporter,
        ILogger<HttpTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(httpOptions);
        ArgumentNullException.ThrowIfNull(safetyOptions);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(contentTrustService);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _httpClientFactory = httpClientFactory;
        _httpOptions = httpOptions;
        _safetyOptions = safetyOptions;
        _guardrailsOptions = guardrailsOptions;
        _contentTrustService = contentTrustService;
        _incidentReporter = incidentReporter;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Description("Performs an HTTP GET request to an allowed host.")]
    public Task<string> GetAsync(
        [Description("Absolute HTTP or HTTPS URL.")] string url,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "http.get",
            new { url },
            async () =>
            {
                var uri = ValidateUri(url);
                using var request = CreateRequest(HttpMethod.Get, uri);
                using var response = await SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                var body = await ReadCappedStringAsync(response, cancellationToken)
                    .ConfigureAwait(false);
                var protectedBody = await ProtectContentAsync(
                        body,
                        "http.get",
                        cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(
                    new
                    {
                        url,
                        statusCode = (int)response.StatusCode,
                        contentType = response.Content.Headers.ContentType?.MediaType,
                        body = protectedBody.Content,
                        protectedBody.ContentEncoding,
                        protectedBody.TrustScore,
                        protectedBody.TrustStatus,
                        protectedBody.Screened,
                        protectedBody.SuspiciousIndicatorCount,
                        protectedBody.ProviderName,
                        protectedBody.ScreeningLatencyMs,
                        protectedBody.ContentProcessingStatus,
                        protectedBody.RequiresHumanReview,
                        protectedBody.OriginalCharacterCount,
                        protectedBody.DeliveredCharacterCount,
                    });
            });

    /// <inheritdoc />
    [Description("Performs an HTTP POST request when Safety:AllowHttpPost is enabled.")]
    public Task<string> PostAsync(
        [Description("Absolute HTTP or HTTPS URL.")] string url,
        [Description("Request body.")] string body,
        [Description("Request media type, such as application/json.")] string contentType,
        CancellationToken cancellationToken)
    {
        if (!_safetyOptions.Value.AllowHttpPost)
        {
            throw new SafetyException("http.post", "Safety:AllowHttpPost");
        }

        ArgumentNullException.ThrowIfNull(body);
        if (!MediaTypeHeaderValue.TryParse(contentType, out _))
        {
            throw new ArgumentException("A valid content type is required.", nameof(contentType));
        }

        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "http.post",
            new { url, contentType, bodyLength = body.Length },
            async () =>
            {
                var uri = ValidateUri(url);
                using var request = CreateRequest(HttpMethod.Post, uri);
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
                using var response = await SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                var responseBody = await ReadCappedStringAsync(response, cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(
                    new
                    {
                        url = uri.AbsoluteUri,
                        statusCode = (int)response.StatusCode,
                        contentType = response.Content.Headers.ContentType?.MediaType,
                        body = responseBody,
                    });
            });
    }

    /// <inheritdoc />
    [Description("Downloads an allowed web page and extracts compact readable text.")]
    public Task<string> GetTextAsync(
        [Description("Absolute HTTP or HTTPS URL.")] string url,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "http.getText",
            new { url },
            async () =>
            {
                var uri = ValidateUri(url);
                using var request = CreateRequest(HttpMethod.Get, uri);
                using var response = await SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                var body = await ReadCappedStringAsync(response, cancellationToken)
                    .ConfigureAwait(false);
                var text = ExtractText(body);
                var protectedText = await ProtectContentAsync(
                        text,
                        "http.getText",
                        cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(
                    new
                    {
                        url,
                        statusCode = (int)response.StatusCode,
                        contentType = response.Content.Headers.ContentType?.MediaType,
                        text = protectedText.Content,
                        protectedText.ContentEncoding,
                        protectedText.TrustScore,
                        protectedText.TrustStatus,
                        protectedText.Screened,
                        protectedText.SuspiciousIndicatorCount,
                        protectedText.ProviderName,
                        protectedText.ScreeningLatencyMs,
                        protectedText.ContentProcessingStatus,
                        protectedText.RequiresHumanReview,
                        protectedText.OriginalCharacterCount,
                        protectedText.DeliveredCharacterCount,
                    });
            });

    private async Task<ProtectedHttpContent> ProtectContentAsync(
        string content,
        string alias,
        CancellationToken cancellationToken)
    {
        var options = _guardrailsOptions.CurrentValue;
        if (content.Length <= options.MaxContentChars)
        {
            var trust = await ScreenWithFallbackAsync(
                    content,
                    $"{alias} guardrail screening",
                    cancellationToken)
                .ConfigureAwait(false);
            var originalDelivery = GuardrailDelivery.Apply(content, trust, options);
            return CreateProtectedContent(
                originalDelivery,
                trust,
                "Original",
                requiresHumanReview: false,
                content.Length,
                content.Length,
                trust.SuspiciousIndicators.Count,
                trust.LatencyMs);
        }

        ContentSummaryResult summary;
        try
        {
            summary = await _contentTrustService
                .SummarizeAsync(
                    content,
                    options.MaxSummaryChars,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GuardrailSummarizationException exception)
        {
            await ReportGuardrailIncidentAsync(
                    exception,
                    $"{alias} guardrail summarization")
                .ConfigureAwait(false);
            return CreateSkippedContent(
                SummarizationFailedMarker,
                "SkippedSummarizationFailed",
                content.Length);
        }

        if (summary.Status is not (
                ContentSummaryStatus.Summarized or
                ContentSummaryStatus.SummaryTruncated))
        {
            return summary.Status switch
            {
                ContentSummaryStatus.SourceTooLarge => CreateSkippedContent(
                    TooLargeMarker,
                    "SkippedTooLarge",
                    content.Length),
                ContentSummaryStatus.LlmProcessingDisabled => CreateSkippedContent(
                    LlmDisabledMarker,
                    "SkippedLlmProcessingDisabled",
                    content.Length),
                ContentSummaryStatus.SummarizationDisabled => CreateSkippedContent(
                    SummarizationDisabledMarker,
                    "SkippedSummarizationDisabled",
                    content.Length),
                _ => throw new InvalidOperationException(
                    $"Unsupported HTTP summary status '{summary.Status}'."),
            };
        }

        var finalTrust = await ScreenWithFallbackAsync(
                summary.Summary,
                $"{alias} guardrail final-summary screening",
                cancellationToken)
            .ConfigureAwait(false);
        var indicators = GuardrailDelivery.UnionIndicators(
            summary.SuspiciousIndicators,
            finalTrust.SuspiciousIndicators);
        if (finalTrust.Screened)
        {
            finalTrust = finalTrust with
            {
                TrustScore = Math.Min(
                    summary.SourceTrustScore
                        ?? throw new InvalidOperationException(
                            "A successful summary must contain a source trust score."),
                    finalTrust.TrustScore
                        ?? throw new InvalidOperationException(
                            "A screened final summary must contain a trust score.")),
                SuspiciousIndicators = indicators,
            };
        }

        var truncated = summary.Status == ContentSummaryStatus.SummaryTruncated;
        var summaryDelivery = GuardrailDelivery.Apply(
            summary.Summary,
            finalTrust,
            options,
            forceBase64: truncated);
        return CreateProtectedContent(
            summaryDelivery,
            finalTrust,
            truncated ? "SummaryTruncated" : "Summarized",
            requiresHumanReview: truncated,
            content.Length,
            summary.Summary.Length,
            indicators.Count,
            summary.LatencyMs + finalTrust.LatencyMs);
    }

    private async Task<ContentTrustResult> ScreenWithFallbackAsync(
        string content,
        string incidentSource,
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
            await ReportGuardrailIncidentAsync(exception, incidentSource)
                .ConfigureAwait(false);
            return exception.FallbackResult;
        }
    }

    private async Task ReportGuardrailIncidentAsync(
        Exception exception,
        string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static ProtectedHttpContent CreateProtectedContent(
        GuardrailDelivery.ProtectedContent delivery,
        ContentTrustResult trust,
        string processingStatus,
        bool requiresHumanReview,
        int originalCharacterCount,
        int deliveredCharacterCount,
        int suspiciousIndicatorCount,
        long latencyMs) =>
        new(
            delivery.Content,
            delivery.ContentEncoding,
            trust.TrustScore,
            trust.Status.ToString(),
            trust.Screened,
            suspiciousIndicatorCount,
            trust.ProviderName,
            latencyMs,
            processingStatus,
            requiresHumanReview,
            originalCharacterCount,
            deliveredCharacterCount);

    private static ProtectedHttpContent CreateSkippedContent(
        string marker,
        string processingStatus,
        int originalCharacterCount) =>
        new(
            marker,
            "none",
            null,
            null,
            false,
            0,
            "None",
            0,
            processingStatus,
            true,
            originalCharacterCount,
            marker.Length);

    private Uri ValidateUri(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "An absolute HTTP or HTTPS URL is required.",
                nameof(url));
        }

        var allowedHosts = _httpOptions.Value.AllowedHosts ?? [];
        if (allowedHosts.Length > 0 &&
            !allowedHosts.Any(
                host => string.Equals(host, uri.IdnHost, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SafetyException(
                $"HTTP request to host '{uri.IdnHost}'",
                "Http:AllowedHosts");
        }

        return uri;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(_httpOptions.Value.UserAgent))
        {
            request.Headers.UserAgent.ParseAdd(_httpOptions.Value.UserAgent);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(_httpOptions.Value.TimeoutSeconds);
        using var timeoutSource = new CancellationTokenSource(timeout, _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linkedSource.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task<string> ReadCappedStringAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        var maximumLength = _httpOptions.Value.MaxResponseCharacters;
        return content.Length <= maximumLength
            ? content
            : content[..maximumLength];
    }

    private string ExtractText(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var node in document.DocumentNode.SelectNodes("//script|//style|//noscript")
                     ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        var decoded = WebUtility.HtmlDecode(document.DocumentNode.InnerText);
        var compact = WhitespacePattern().Replace(decoded, " ").Trim();
        var maximumLength = _httpOptions.Value.MaxResponseCharacters;
        return compact.Length <= maximumLength ? compact : compact[..maximumLength];
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    private sealed record ProtectedHttpContent(
        string Content,
        string ContentEncoding,
        int? TrustScore,
        string? TrustStatus,
        bool Screened,
        int SuspiciousIndicatorCount,
        string ProviderName,
        long ScreeningLatencyMs,
        string ContentProcessingStatus,
        bool RequiresHumanReview,
        int OriginalCharacterCount,
        int DeliveredCharacterCount);
}
