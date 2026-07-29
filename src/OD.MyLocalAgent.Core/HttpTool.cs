using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements allowlisted compact-JSON HTTP tools.
/// </summary>
public sealed partial class HttpTool : IHttpTool
{
    /// <summary>
    /// Gets the managed HTTP-client name.
    /// </summary>
    public const string HttpClientName = "OD.MyLocalAgent.HttpTool";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<HttpOptions> _httpOptions;
    private readonly IOptions<SafetyOptions> _safetyOptions;
    private readonly ILogger<HttpTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpTool"/> class.
    /// </summary>
    public HttpTool(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpOptions> httpOptions,
        IOptions<SafetyOptions> safetyOptions,
        ILogger<HttpTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(httpOptions);
        ArgumentNullException.ThrowIfNull(safetyOptions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _httpClientFactory = httpClientFactory;
        _httpOptions = httpOptions;
        _safetyOptions = safetyOptions;
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
                return ToolJson.Serialize(
                    new
                    {
                        url = uri.AbsoluteUri,
                        statusCode = (int)response.StatusCode,
                        contentType = response.Content.Headers.ContentType?.MediaType,
                        body,
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
                return ToolJson.Serialize(
                    new
                    {
                        url = uri.AbsoluteUri,
                        statusCode = (int)response.StatusCode,
                        text,
                    });
            });

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
}
