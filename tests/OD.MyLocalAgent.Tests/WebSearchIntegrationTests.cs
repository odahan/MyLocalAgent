using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Integration tests that make real network calls to the Ollama Web Search API.
/// These tests require a valid API key stored in the DPAPI-protected secrets.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WebSearchIntegrationTests : IAsyncLifetime
{
    private ServiceProvider? _serviceProvider;
    private IWebSearchConnector? _connector;
    private bool _isConfigured;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        try
        {
            var config = await LoadRealConfigurationAsync();
            var options = config.GetSection(WebSearchOptions.SectionName).Get<WebSearchOptions>();

            if (options is null || string.IsNullOrWhiteSpace(options.ApiKey))
            {
                _isConfigured = false;
                return;
            }

            var services = new ServiceCollection();
            services.AddConnectors(config);
            _serviceProvider = services.BuildServiceProvider();

            _connector = _serviceProvider.GetRequiredService<IWebSearchConnector>();
            _isConfigured = true;
        }
        catch
        {
            _isConfigured = false;
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Verifies web search returns results for a .NET query.
    /// </summary>
    [Fact]
    public async Task WebSearchReturnsResults()
    {
        if (!_isConfigured)
        {
            await Task.CompletedTask;
            return;
        }

        var result = await _connector!.SearchAsync(
            ".NET 10 release",
            3,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Items.Should().NotBeEmpty();
        result.Items.Count.Should().BeLessOrEqualTo(3);
        result.Items[0].Title.Should().NotBeNullOrWhiteSpace();
        result.Items[0].Url.Should().StartWith("http");
    }

    /// <summary>
    /// Verifies web search returns results for a minimal query.
    /// </summary>
    [Fact]
    public async Task WebSearchWithMinimalQueryReturnsResults()
    {
        if (!_isConfigured)
        {
            await Task.CompletedTask;
            return;
        }

        var result = await _connector!.SearchAsync(
            "C# programming",
            1,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Items.Should().ContainSingle();
        result.Items[0].Title.Should().NotBeNullOrWhiteSpace();
        result.Items[0].Url.Should().StartWith("http");
    }

    /// <summary>
    /// Verifies web search returns results for a French query.
    /// </summary>
    [Fact]
    public async Task WebSearchWithFrenchQueryReturnsResults()
    {
        if (!_isConfigured)
        {
            await Task.CompletedTask;
            return;
        }

        var result = await _connector!.SearchAsync(
            "actualités informatiques France",
            2,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Items.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies web search returns results for a site-specific query.
    /// </summary>
    [Fact]
    public async Task WebSearchWithSpecificSiteReturnsResults()
    {
        if (!_isConfigured)
        {
            await Task.CompletedTask;
            return;
        }

        var result = await _connector!.SearchAsync(
            "site:github.com dotnet runtime",
            2,
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Items.Should().NotBeEmpty();
        result.Items.Should().AllSatisfy(item =>
            item.Url.Should().Contain("github.com"));
    }

    /// <summary>
    /// Verifies web search connector reports the correct provider name.
    /// </summary>
    [Fact]
    public async Task WebSearchConnectorReportsProviderName()
    {
        if (!_isConfigured)
        {
            await Task.CompletedTask;
            return;
        }

        var result = await _connector!.SearchAsync(
            "test query",
            1,
            CancellationToken.None);

        result.Provider.Should().Be(WebSearchOptions.OllamaProvider);
    }

    private static async Task<IConfigurationRoot> LoadRealConfigurationAsync()
    {
        var settingsPath = LocalAppPaths.UserSettingsPath;
        var builder = new ConfigurationBuilder();

        if (File.Exists(settingsPath))
        {
            builder.AddJsonFile(settingsPath, optional: true, reloadOnChange: false);
        }

        var secretStore = new DpapiSecretStore();
        var secrets = await secretStore.ReadAllAsync(CancellationToken.None);

        var secretDict = new Dictionary<string, string?>();
        foreach (var kvp in secrets)
        {
            secretDict[kvp.Key] = kvp.Value;
        }

        builder.AddInMemoryCollection(secretDict);

        return builder.Build();
    }
}
