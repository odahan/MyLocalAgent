using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.UI.Services;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies non-destructive user-settings persistence and secret separation.
/// </summary>
public sealed class UserSettingsServiceTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Tests",
            Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies Web Search and general saves preserve each other's sections.
    /// </summary>
    [Fact]
    public async Task SaveOperationsMergeSectionsAndKeepWebSearchKeySecret()
    {
        Directory.CreateDirectory(_directoryPath);
        var settingsPath = Path.Combine(_directoryPath, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "Custom": {
                "Keep": "yes"
              },
              "WebSearch": {
                "Provider": "",
                "Endpoint": "https://old.example.test/search",
                "DefaultMaxResults": 7,
                "MaxResultsCap": 9,
                "TimeoutSeconds": 12
              }
            }
            """);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false, reloadOnChange: false)
            .Build();
        var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
        secrets
            .Setup(
                instance => instance.SetAsync(
                    SecretKeys.WebSearchApiKey,
                    "web-secret",
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = new UserSettingsService(
            configuration,
            secrets.Object,
            settingsPath);

        await service.SaveWebSearchAsync(
            WebSearchOptions.OllamaProvider,
            "https://ollama.com/api/web_search",
            " web-secret ",
            CancellationToken.None);
        await service.SaveAsync(
            ValidSettings(),
            replacementApiKey: null,
            CancellationToken.None);
        await service.SaveWebSearchAsync(
            WebSearchOptions.OllamaProvider,
            "https://ollama.com/api/web_search",
            replacementApiKey: null,
            CancellationToken.None);

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(settingsPath));
        var root = document.RootElement;
        root.GetProperty("Custom").GetProperty("Keep").GetString()
            .Should().Be("yes");
        var webSearch = root.GetProperty(WebSearchOptions.SectionName);
        webSearch.GetProperty("Provider").GetString()
            .Should().Be(WebSearchOptions.OllamaProvider);
        webSearch.GetProperty("Endpoint").GetString()
            .Should().Be("https://ollama.com/api/web_search");
        webSearch.GetProperty("DefaultMaxResults").GetInt32().Should().Be(7);
        webSearch.GetProperty("MaxResultsCap").GetInt32().Should().Be(9);
        webSearch.GetProperty("TimeoutSeconds").GetInt32().Should().Be(12);
        webSearch.TryGetProperty("ApiKey", out _).Should().BeFalse();
        root.GetProperty("AI").GetProperty("Provider").GetString()
            .Should().Be(AIOptions.OllamaProvider);
        secrets.VerifyAll();
        Directory.GetFiles(_directoryPath, "*.tmp")
            .Should().BeEmpty();
    }

    /// <summary>
    /// Verifies invalid Web Search settings do not mutate disk or secrets.
    /// </summary>
    [Theory]
    [InlineData("Unsupported", "https://ollama.com/api/web_search")]
    [InlineData("Ollama", "relative/path")]
    public async Task InvalidWebSearchSettingsAreRejectedBeforePersistence(
        string provider,
        string endpoint)
    {
        Directory.CreateDirectory(_directoryPath);
        var settingsPath = Path.Combine(_directoryPath, "settings.json");
        const string original = """{"Custom":{"Keep":"yes"}}""";
        await File.WriteAllTextAsync(settingsPath, original);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false, reloadOnChange: false)
            .Build();
        var secrets = new Mock<ISecretStore>(MockBehavior.Strict);
        var service = new UserSettingsService(
            configuration,
            secrets.Object,
            settingsPath);

        var action = () => service.SaveWebSearchAsync(
            provider,
            endpoint,
            "secret",
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(settingsPath)).Should().Be(original);
        secrets.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Deletes the isolated settings directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    private static UserSettingsData ValidSettings() =>
        new(
            AIOptions.OllamaProvider,
            "http://localhost:11434",
            "qwen3:8b",
            120,
            "https://api.openai.com/v1",
            "gpt-5-mini",
            120,
            Path.GetTempPath(),
            30,
            "OD.MyLocalAgent/1.0",
            [],
            "\\OD.MyLocalAgent",
            false,
            string.Empty,
            false,
            false,
            false,
            true,
            80,
            50,
            8_000,
            true,
            4_000,
            100_000,
            60,
            true,
            0,
            "Information",
            AppThemeCatalog.DarkName);
}
