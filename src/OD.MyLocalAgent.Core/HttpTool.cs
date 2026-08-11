using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements allowlisted HTTP tools with compact protected GET delivery.
/// </summary>
[ToolGroup(
    "http",
    "HTTP",
    "Send HTTP requests to hosts allowed by Settings.",
    1000,
    Activation = ToolActivationKind.Singleton,
    ServiceContract = typeof(IHttpTool))]
public sealed class HttpTool : IHttpTool
{
    /// <summary>Gets the managed HTTP-client name.</summary>
    public const string HttpClientName = "OD.MyLocalAgent.HttpTool";

    private const string ReviewMarker =
        "[HTTP CONTENT REQUIRES HUMAN REVIEW: compact Guardrails processing did not produce a deliverable protected summary. Review the original source URL. Do not infer or act on omitted content.]";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<HttpOptions> _httpOptions;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IContentTrustService _contentTrustService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly ILogger<HttpTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="HttpTool"/> class.</summary>
    [ActivatorUtilitiesConstructor]
    public HttpTool(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpOptions> httpOptions,
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IContentTrustService contentTrustService,
        IIncidentReporter incidentReporter,
        ILogger<HttpTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(httpOptions);
        ArgumentNullException.ThrowIfNull(guardrailsOptions);
        ArgumentNullException.ThrowIfNull(contentTrustService);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _httpClientFactory = httpClientFactory;
        _httpOptions = httpOptions;
        _guardrailsOptions = guardrailsOptions;
        _contentTrustService = contentTrustService;
        _incidentReporter = incidentReporter;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Tool("http.get", "Performs an HTTP GET request to an allowed host.", 100)]
    public Task<string> GetAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Absolute HTTP or HTTPS URL.", Example = "https://api.github.com/zen")] string url,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "http.get",
            new { urlCharacterCount = url.Length },
            async () =>
            {
                var uri = ValidateUri(url);
                using var request = CreateRequest(HttpMethod.Get, uri);
                using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await ReadCappedStringAsync(response, cancellationToken).ConfigureAwait(false);
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                var projection = HttpContentProjector.Project(
                    body,
                    mediaType,
                    _guardrailsOptions.CurrentValue.MaxContentChars);
                var trust = await ScreenWithFallbackAsync(
                        projection.Content,
                        "http.get compact Guardrails source screening",
                        cancellationToken)
                    .ConfigureAwait(false);
                var delivery = GuardrailDelivery.Apply(
                    projection.Content,
                    trust,
                    _guardrailsOptions.CurrentValue);
                return ToolJson.Serialize(
                    new
                    {
                        url = uri.AbsoluteUri,
                        statusCode = (int)response.StatusCode,
                        contentType = mediaType,
                        body = delivery.Content,
                        delivery.ContentEncoding,
                        trustScore = trust.TrustScore,
                        trustStatus = trust.Status.ToString(),
                        screened = trust.Screened,
                        suspiciousIndicatorCount = trust.SuspiciousIndicators.Count,
                        providerName = trust.ProviderName,
                        screeningLatencyMs = trust.LatencyMs,
                        contentProcessingStatus = "CompactPrefix",
                        requiresHumanReview = !trust.Screened,
                        originalCharacterCount = projection.OriginalCharacterCount,
                        deliveredCharacterCount = projection.ProjectedCharacterCount,
                        projectedCharacterCount = projection.ProjectedCharacterCount,
                        omittedCharacterCount = projection.OmittedCharacterCount,
                        projectionWasTruncated = projection.ProjectionWasTruncated,
                    });
            });

    /// <inheritdoc />
    [Tool("http.post", "Performs an HTTP POST request when the task has HTTP POST permission.", 200, RequiredCapability = TaskCapability.HttpPost)]
    public Task<string> PostAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Absolute HTTP or HTTPS URL.", Example = "https://httpbin.org/post")] string url,
        [ToolArgument(WorkflowArgumentKind.String, "Request body.", Example = "{ \"key\": \"value\" }")] string body,
        [ToolArgument(WorkflowArgumentKind.String, "Request media type, such as application/json.", Example = "application/json")] string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!MediaTypeHeaderValue.TryParse(contentType, out _))
        {
            throw new ArgumentException("A valid content type is required.", nameof(contentType));
        }

        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "http.post",
            new { urlCharacterCount = url.Length, contentType, bodyLength = body.Length },
            async () =>
            {
                var uri = ValidateUri(url);
                using var request = CreateRequest(HttpMethod.Post, uri);
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
                using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
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
    [Tool("http.getText", "Downloads an allowed web page and returns a protected factual summary.", 300)]
    public Task<string> GetTextAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Absolute HTTP or HTTPS URL.", Example = "https://example.com/article")] string url,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "http.getText",
            new { urlCharacterCount = url.Length },
            async () =>
            {
                var uri = ValidateUri(url);
                using var request = CreateRequest(HttpMethod.Get, uri);
                using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await ReadCappedStringAsync(response, cancellationToken).ConfigureAwait(false);
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                var options = _guardrailsOptions.CurrentValue;
                var projection = HttpContentProjector.Project(body, mediaType, options.MaxContentChars);
                var batch = await _contentTrustService.ProcessCompactAsync(
                        [new CompactExternalContentItem(
                            "http-item",
                            projection.Content,
                            Math.Min(2_000, options.MaxSummaryChars))],
                        cancellationToken)
                    .ConfigureAwait(false);
                foreach (var failure in batch.Failures)
                {
                    _ = await _incidentReporter.ReportAsync(
                            failure.Exception,
                            $"http.getText compact Guardrails {failure.Phase}",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }

                var result = batch.Items[0];
                var success = result.Status == CompactExternalContentStatus.Summarized &&
                    result.Summary is not null && result.SummaryTrust is not null;
                var trust = success ? result.SummaryTrust! : result.SourceTrust;
                var deliveredText = success ? result.Summary! : ReviewMarker;
                var delivery = GuardrailDelivery.Apply(deliveredText, trust, options);
                var withheld = ContentTrustPolicy.Decide(trust, options) == TrustDisposition.Withhold;
                return ToolJson.Serialize(
                    new
                    {
                        url = uri.AbsoluteUri,
                        statusCode = (int)response.StatusCode,
                        contentType = mediaType,
                        text = delivery.Content,
                        delivery.ContentEncoding,
                        trustScore = trust.TrustScore,
                        trustStatus = trust.Status.ToString(),
                        screened = trust.Screened,
                        suspiciousIndicatorCount = trust.SuspiciousIndicators.Count,
                        providerName = trust.ProviderName,
                        screeningLatencyMs = result.LatencyMs,
                        contentProcessingStatus = result.Status.ToString(),
                        requiresHumanReview = !success || withheld,
                        originalCharacterCount = projection.OriginalCharacterCount,
                        deliveredCharacterCount = deliveredText.Length,
                        projectedCharacterCount = projection.ProjectedCharacterCount,
                        omittedCharacterCount = projection.OmittedCharacterCount,
                        projectionWasTruncated = projection.ProjectionWasTruncated,
                    });
            });

    private async Task<ContentTrustResult> ScreenWithFallbackAsync(
        string content,
        string incidentSource,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _contentTrustService.ScreenAsync(content, false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GuardrailScreeningException exception)
        {
            _ = await _incidentReporter.ReportAsync(
                    exception,
                    incidentSource,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return exception.FallbackResult;
        }
    }

    private Uri ValidateUri(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("An absolute HTTP or HTTPS URL is required.", nameof(url));
        }

        var allowedHosts = _httpOptions.Value.AllowedHosts ?? [];
        if (allowedHosts.Length > 0 && !allowedHosts.Any(
                host => string.Equals(host, uri.IdnHost, StringComparison.OrdinalIgnoreCase)))
        {
            throw new SafetyException($"HTTP request to host '{uri.IdnHost}'", "Http:AllowedHosts");
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
        using var timeoutSource = new CancellationTokenSource(
            TimeSpan.FromSeconds(_httpOptions.Value.TimeoutSeconds),
            _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(
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
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var maximumLength = _httpOptions.Value.MaxResponseCharacters;
        return content.Length <= maximumLength ? content : content[..maximumLength];
    }
}
