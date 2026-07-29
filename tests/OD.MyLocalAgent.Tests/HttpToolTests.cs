using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
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
            .Should().Be("Hello world");
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

    private static HttpTool CreateTool(
        HttpMessageHandler handler,
        string[] allowedHosts,
        bool allowPost) =>
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
            NullLogger<HttpTool>.Instance,
            TimeProvider.System);
}
