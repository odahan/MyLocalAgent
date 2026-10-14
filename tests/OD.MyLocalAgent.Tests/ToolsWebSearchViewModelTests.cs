using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the web-search settings shown to the user match the provider being edited.</summary>
public sealed class ToolsWebSearchViewModelTests
{
    /// <summary>Verifies fresh defaults, legacy selection and disabled provider preferences.</summary>
    [Theory]
    [InlineData("", "Tavily", "Tavily", false)]
    [InlineData("Ollama", "Tavily", "Ollama", true)]
    [InlineData("", "Ollama", "Ollama", false)]
    public async Task RefreshPreservesProviderSelection(string provider, string preferred, string expected, bool enabled)
    {
        var viewModel = Create(new WebSearchOptions { Provider = provider, PreferredProvider = preferred });
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.WebSearchProvider.Should().Be(expected);
        viewModel.IsWebSearchEnabled.Should().Be(enabled);
        viewModel.WebSearchEndpoint.Should().Be(expected == "Tavily"
            ? "https://api.tavily.com/search" : "https://ollama.com/api/web_search");
    }

    /// <summary>Verifies switching cannot carry a replacement key into the other provider.</summary>
    [Fact]
    public async Task SwitchingUpdatesKeyStatusAndRetainsEndpointDrafts()
    {
        var secrets = new Mock<ISecretStore>();
        secrets.Setup(store => store.ExistsAsync(SecretKeys.WebSearchApiKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var viewModel = Create(new WebSearchOptions(), secrets: secrets);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.IsWebSearchConfigured.Should().BeFalse();
        viewModel.WebSearchEndpoint = "https://custom.example/search";
        viewModel.WebSearchApiKeyReplacement = "tavily-draft";
        viewModel.WebSearchProvider = "Ollama";
        viewModel.IsWebSearchConfigured.Should().BeTrue();
        viewModel.WebSearchApiKeyReplacement.Should().BeEmpty();
        viewModel.WebSearchEndpoint.Should().Be("https://ollama.com/api/web_search");
        viewModel.WebSearchProvider = "Tavily";
        viewModel.WebSearchEndpoint.Should().Be("https://custom.example/search");
        viewModel.IsWebSearchConfigured.Should().BeFalse();
    }

    /// <summary>Verifies an unsaved provider change cannot test the previously active provider.</summary>
    [Fact]
    public async Task TestRejectsUnsavedProviderChange()
    {
        var connector = new Mock<IWebSearchConnector>(MockBehavior.Strict);
        var viewModel = Create(new WebSearchOptions { Provider = "Ollama" }, connector);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.WebSearchProvider = "Tavily";
        await viewModel.TestWebSearchCommand.ExecuteAsync(null);
        viewModel.WebSearchTestState.Should().Be("Unhealthy");
        viewModel.WebSearchTestStatus.Should().Contain("save the selected provider");
        connector.VerifyNoOtherCalls();
    }

    private static ToolsViewModel Create(WebSearchOptions options,
        Mock<IWebSearchConnector>? connector = null, Mock<ISecretStore>? secrets = null) =>
        new(Mock.Of<IGoogleCredentialProvider>(), Mock.Of<IGmailConnector>(),
            Mock.Of<ICalendarConnector>(), Mock.Of<IDropboxConnector>(),
            (connector ?? new Mock<IWebSearchConnector>()).Object,
            new MutableOptionsMonitor<WebSearchOptions>(options),
            (secrets ?? new Mock<ISecretStore>()).Object, Mock.Of<IUserSettingsService>(),
            new ConfigurationBuilder().AddInMemoryCollection().Build(), Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(), new WeakReferenceMessenger(), TimeProvider.System);
}
