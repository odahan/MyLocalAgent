using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies HTTP allowlisting, text extraction, result shape, and POST gating.
/// </summary>
public sealed class HttpToolTests
{
    /// <summary>
    /// Verifies an allowed GET and HTML text extraction.
    /// </summary>
    [Fact]
    public async Task AllowedGetAndGetTextReturnCompactJson()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "<html><style>hidden</style><body>Hello <b>world</b><script>bad()</script></body></html>",
                        Encoding.UTF8,
                        "text/html"),
                }));
        var tool = CreateTool(handler, ["example.com"], allowPost: false);

        var getJson = await tool.GetAsync(
            "https://example.com/page",
            CancellationToken.None);
        var textJson = await tool.GetTextAsync(
            "https://example.com/page",
            CancellationToken.None);

        JsonDocument.Parse(getJson).RootElement.GetProperty("statusCode").GetInt32()
            .Should().Be(200);
        JsonDocument.Parse(textJson).RootElement.GetProperty("text").GetString()
            .Should().Contain("Hello world");
    }

    /// <summary>
    /// Verifies disallowed hosts and disabled POST requests fail before transport.
    /// </summary>
    [Fact]
    public async Task AllowlistAndPostSafetyGatesAreEnforced()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("Transport should not be called."));
        var tool = CreateTool(handler, ["example.com"], allowPost: false);

        var disallowedGet = () => tool.GetAsync(
            "https://other.example/path",
            CancellationToken.None);
        var blockedPost = () => tool.PostAsync(
            "https://example.com/path",
            "{}",
            "application/json",
            CancellationToken.None);

        await disallowedGet.Should().ThrowAsync<SafetyException>()
            .Where(exception => exception.RequiredFlag == "Http:AllowedHosts");
        await blockedPost.Should().ThrowAsync<SafetyException>()
            .Where(exception => exception.RequiredFlag == "Safety:AllowHttpPost");
    }

    /// <summary>
    /// Verifies an enabled POST maps the request body and content type.
    /// </summary>
    [Fact]
    public async Task EnabledPostMapsBodyAndContentType()
    {
        string? observedBody = null;
        string? observedContentType = null;
        var handler = new StubHttpMessageHandler(
            async (request, cancellationToken) =>
            {
                observedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                observedContentType = request.Content.Headers.ContentType!.MediaType;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json"),
                };
            });
        var tool = CreateTool(handler, [], allowPost: true);

        var json = await tool.PostAsync(
            "https://example.com/path",
            "{\"value\":1}",
            "application/json",
            CancellationToken.None);

        observedBody.Should().Be("{\"value\":1}");
        observedContentType.Should().Be("application/json");
        JsonDocument.Parse(json).RootElement.GetProperty("statusCode").GetInt32()
            .Should().Be(201);
    }

    /// <summary>
    /// Verifies http.get delivers one bounded compact prefix without summarization.
    /// </summary>
    [Fact]
    public async Task OversizedSourceReturnsProtectedCompactPrefix()
    {
        var source = new string('q', 5_000);
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        source,
                        Encoding.UTF8,
                        "text/plain"),
                }));
        var trustService = new StubContentTrustService();
        var guardrails = new GuardrailsOptions
        {
            MaxContentChars = 2_048,
            MaxSummaryChars = 256,
            MaxSummarizationSourceChars = 6_000,
        };
        var tool = CreateTool(
            handler,
            ["example.com"],
            allowPost: false,
            guardrails,
            trustService);

        var json = await tool.GetAsync(
            "https://example.com/large",
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("contentProcessingStatus").GetString()
            .Should().Be("CompactPrefix");
        document.RootElement.GetProperty("body").GetString()
            .Should().Contain("characters omitted");
        document.RootElement.GetProperty("projectionWasTruncated").GetBoolean()
            .Should().BeTrue();
        trustService.SummarizedContents.Should().BeEmpty();
        trustService.CompactBatches.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a compact getText phase failure is reported once and returns a review marker.
    /// </summary>
    [Fact]
    public async Task SummarizationFailureIsReportedOnceAndReturnsSkipMarker()
    {
        var source = new string('r', 5_000);
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(source, Encoding.UTF8, "text/plain"),
                }));
        var trustService = new StubContentTrustService
        {
            CompactHandler = (items, _) =>
            {
                var exception = new CompactExternalContentPhaseException(
                    "Summarization", "Stub", "test-model", 1, 5);
                return Task.FromResult(
                    new CompactExternalContentBatchResult(
                        [new CompactExternalContentResult(
                            items[0].Id,
                            null,
                            Trust(items[0].Content, 95),
                            null,
                            CompactExternalContentStatus.SummarizationUnavailable,
                            true,
                            5)],
                        [new CompactExternalContentBatchFailure(
                            "Summarization", exception, 1)]));
            },
        };
        var reporter = new StubIncidentReporter();
        var guardrails = new GuardrailsOptions
        {
            MaxContentChars = 2_048,
            MaxSummaryChars = 256,
            MaxSummarizationSourceChars = 6_000,
        };
        var tool = CreateTool(
            handler,
            ["example.com"],
            allowPost: false,
            guardrails,
            trustService,
            reporter);

        var json = await tool.GetTextAsync(
            "https://example.com/failure",
            CancellationToken.None);

        reporter.Reports.Should().ContainSingle();
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("contentProcessingStatus").GetString()
            .Should().Be("SummarizationUnavailable");
        document.RootElement.GetProperty("text").GetString()
            .Should().Contain("HTTP CONTENT REQUIRES HUMAN REVIEW");
        json.Should().NotContain(new string('r', 20));
    }

    /// <summary>
    /// Verifies http.get never invokes the compact summary pipeline.
    /// </summary>
    [Fact]
    public async Task GetNeverInvokesCompactSummarization()
    {
        var source = new string('s', 500);
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(source, Encoding.UTF8, "text/plain"),
                }));
        var trustService = new StubContentTrustService();
        var guardrails = new GuardrailsOptions
        {
            MaxContentChars = 2_048,
            MaxSummaryChars = 256,
            MaxSummarizationSourceChars = 1_000,
        };
        var tool = CreateTool(
            handler,
            ["example.com"],
            allowPost: false,
            guardrails,
            trustService);

        var json = await tool.GetAsync(
            "https://example.com/truncated",
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("contentProcessingStatus").GetString()
            .Should().Be("CompactPrefix");
        document.RootElement.GetProperty("contentEncoding").GetString()
            .Should().Be("none");
        trustService.CompactBatches.Should().BeEmpty();
        trustService.SummarizedContents.Should().BeEmpty();
    }

    private static HttpTool CreateTool(
        HttpMessageHandler handler,
        string[] allowedHosts,
        bool allowPost,
        GuardrailsOptions? guardrailsOptions = null,
        StubContentTrustService? contentTrustService = null,
        StubIncidentReporter? incidentReporter = null) =>
        new(
            new StubHttpClientFactory(handler),
            Options.Create(
                new HttpOptions
                {
                    AllowedHosts = allowedHosts,
                    TimeoutSeconds = 5,
                    MaxResponseCharacters = 10_000,
                }),
            Options.Create(new SafetyOptions { AllowHttpPost = allowPost }),
            new MutableOptionsMonitor<GuardrailsOptions>(
                guardrailsOptions ?? new GuardrailsOptions()),
            contentTrustService ?? new StubContentTrustService(),
            incidentReporter ?? new StubIncidentReporter(),
            NullLogger<HttpTool>.Instance,
            TimeProvider.System);

    private static ContentTrustResult Trust(string content, int score) =>
        new(
            score,
            ContentTrustEnvelope.WrapPlaintext(content),
            "none",
            ContentTrustStatus.Screened,
            [],
            "Stub",
            0);
}
