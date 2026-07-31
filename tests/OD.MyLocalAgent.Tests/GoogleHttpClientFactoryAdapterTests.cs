using FluentAssertions;
using Google.Apis.Http;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Tests.TestDoubles;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the bridge between managed HTTP handlers and Google API clients.
/// </summary>
public sealed class GoogleHttpClientFactoryAdapterTests
{
    /// <summary>
    /// Verifies that a Google request reaches the managed handler without being sent through
    /// a second <see cref="HttpClient"/> instance.
    /// </summary>
    [Fact]
    public async Task GoogleRequestUsesManagedHandlerDirectly()
    {
        HttpRequestMessage? observedRequest = null;
        var managedHandler = new StubHttpMessageHandler(
            (request, _) =>
            {
                observedRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });
        var adapter = new GoogleHttpClientFactoryAdapter(
            new StubHttpMessageHandlerFactory(managedHandler));
        using var client = adapter.CreateHttpClient(new CreateHttpClientArgs());

        using var response = await client.GetAsync(
            "https://oauth2.googleapis.test/token",
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        observedRequest.Should().NotBeNull();
        observedRequest!.RequestUri.Should().Be(
            "https://oauth2.googleapis.test/token");
    }

    /// <summary>
    /// Verifies that Google client initializers can attach OAuth credentials to requests.
    /// </summary>
    [Fact]
    public async Task GoogleRequestAppliesClientInitializers()
    {
        AuthenticationHeaderValue? observedAuthorization = null;
        var managedHandler = new StubHttpMessageHandler(
            (request, _) =>
            {
                observedAuthorization = request.Headers.Authorization;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });
        var adapter = new GoogleHttpClientFactoryAdapter(
            new StubHttpMessageHandlerFactory(managedHandler));
        var args = new CreateHttpClientArgs();
        args.Initializers.Add(new BearerTokenInitializer("test-access-token"));
        using var client = adapter.CreateHttpClient(args);

        using var response = await client.GetAsync(
            "https://gmail.googleapis.test/messages",
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        observedAuthorization.Should().NotBeNull();
        observedAuthorization!.Scheme.Should().Be("Bearer");
        observedAuthorization.Parameter.Should().Be("test-access-token");
    }

    private sealed class BearerTokenInitializer(string accessToken)
        : IConfigurableHttpClientInitializer
    {
        public void Initialize(ConfigurableHttpClient httpClient)
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }
    }
}
