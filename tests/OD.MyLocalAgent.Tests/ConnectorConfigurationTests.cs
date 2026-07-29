using FluentAssertions;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies that missing connector credentials surface as typed configuration failures.
/// </summary>
public sealed class ConnectorConfigurationTests
{
    /// <summary>
    /// Verifies typed Google OAuth validation before credential-store access.
    /// </summary>
    [Fact]
    public async Task GoogleCredentialProviderRejectsMissingClientCredentials()
    {
        var provider = new GoogleCredentialProvider(
            Options.Create(new GoogleOptions()),
            CreateHttpClientFactory());

        var action = () => provider.GetCredentialSilentAsync(CancellationToken.None);

        await action.Should().ThrowAsync<ConnectorConfigurationException>()
            .WithMessage("*Google:ClientId*");
    }

    /// <summary>
    /// Verifies typed Dropbox validation before an outbound API request.
    /// </summary>
    [Fact]
    public async Task DropboxConnectorRejectsMissingTokens()
    {
        var connector = new DropboxConnector(
            new MutableOptionsMonitor<DropboxOptions>(new DropboxOptions()),
            CreateHttpClientFactory());

        var action = () => connector.ListAsync(string.Empty, CancellationToken.None);

        await action.Should().ThrowAsync<ConnectorConfigurationException>()
            .WithMessage("*Dropbox:AccessToken*");
    }

    private static IHttpClientFactory CreateHttpClientFactory()
    {
        return new StubHttpClientFactory(new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException("No HTTP call is expected.")));
    }
}
