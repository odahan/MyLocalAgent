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
              "AI": {
                "FutureSetting": "preserve"
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
        var ai = root.GetProperty("AI");
        ai.GetProperty("Provider").GetString()
            .Should().Be(AIOptions.OllamaProvider);
        ai.GetProperty("FutureSetting").GetString()
            .Should().Be("preserve");
        var generation = ai.GetProperty("GenerationDefaults");
        generation.GetProperty("Temperature").GetDouble().Should().Be(0.2);
        generation.GetProperty("TopP").GetDouble().Should().Be(1);
        generation.GetProperty("ContextWindowTokens").GetInt32()
            .Should().Be(8192);
        root.GetProperty("Safety").GetProperty("AllowMoveToBin")
            .GetBoolean().Should().BeFalse();
        root.GetProperty("Safety").GetProperty("AllowSqliteWrite")
            .GetBoolean().Should().BeFalse();
        var sqlite = root.GetProperty("SQLite");
        sqlite.GetProperty("DefaultMaxRows").GetInt32().Should().Be(100);
        sqlite.GetProperty("MaxRows").GetInt32().Should().Be(1_000);
        sqlite.GetProperty("GuardrailsEnabledByDefault").GetBoolean().Should().BeTrue();
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
    /// Verifies every SQLite setting and its Safety gate persist, preserve unrelated sections, and reload into configuration.
    /// </summary>
    [Fact]
    public async Task SavePersistsAllSqliteSettingsAndReloadsConfiguration()
    {
        Directory.CreateDirectory(_directoryPath);
        var settingsPath = Path.Combine(_directoryPath, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "Custom": { "Keep": "yes" },
              "Secrets": { "Preserve": "unchanged" }
            }
            """);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false, reloadOnChange: false)
            .Build();
        var settings = ValidSettings() with
        {
            AllowSqliteWrite = true,
            SQLiteDefaultMaxRows = 321,
            SQLiteMaxRows = 654,
            SQLiteMaxResultCharacters = 12_345,
            SQLiteMaxBlobBytes = 678,
            SQLiteMaxSqlCharacters = 9_876,
            SQLiteCommandTimeoutSeconds = 45,
            SQLiteGuardrailsEnabledByDefault = false,
        };
        var service = new UserSettingsService(
            configuration,
            Mock.Of<ISecretStore>(),
            settingsPath);

        await service.SaveAsync(settings, replacementApiKey: null, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var root = document.RootElement;
        root.GetProperty("Custom").GetProperty("Keep").GetString().Should().Be("yes");
        root.GetProperty("Secrets").GetProperty("Preserve").GetString().Should().Be("unchanged");
        root.GetProperty("Safety").GetProperty("AllowSqliteWrite").GetBoolean().Should().BeTrue();
        var sqlite = root.GetProperty("SQLite");
        sqlite.GetProperty("DefaultMaxRows").GetInt32().Should().Be(321);
        sqlite.GetProperty("MaxRows").GetInt32().Should().Be(654);
        sqlite.GetProperty("MaxResultCharacters").GetInt32().Should().Be(12_345);
        sqlite.GetProperty("MaxBlobBytes").GetInt32().Should().Be(678);
        sqlite.GetProperty("MaxSqlCharacters").GetInt32().Should().Be(9_876);
        sqlite.GetProperty("CommandTimeoutSeconds").GetInt32().Should().Be(45);
        sqlite.GetProperty("GuardrailsEnabledByDefault").GetBoolean().Should().BeFalse();
        configuration["Safety:AllowSqliteWrite"].Should().Be("True");
        configuration["SQLite:MaxResultCharacters"].Should().Be("12345");
        configuration["SQLite:GuardrailsEnabledByDefault"].Should().Be("False");
    }

    /// <summary>Verifies every Stage 22 file-content value and archive Safety gate persist atomically.</summary>
    [Fact]
    public async Task SavePersistsAllFileContentSettingsAndPreservesUnknownSections()
    {
        Directory.CreateDirectory(_directoryPath);
        var settingsPath = Path.Combine(_directoryPath, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """{"Future":{"Keep":42}}""");
        var configuration = new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false, reloadOnChange: false).Build();
        var settings = ValidSettings() with
        {
            AllowArchiveWrite = true,
            FileContentMaxInputFileBytes = 2_000_000,
            FileContentMaxArchiveEntries = 123,
            FileContentMaxArchiveEntryBytes = 2_000_000,
            FileContentMaxArchiveTotalBytes = 4_000_000,
            FileContentMaxArchiveCompressionRatio = 25,
            FileContentMaxListedEntries = 100,
            FileContentMaxDocumentCharacters = 12_000,
            FileContentMaxSpreadsheetRows = 200,
            FileContentMaxSpreadsheetColumns = 20,
            FileContentMaxSpreadsheetCells = 2_000,
            FileContentMaxPdfPages = 25,
        };
        var service = new UserSettingsService(configuration, Mock.Of<ISecretStore>(), settingsPath);

        await service.SaveAsync(settings, replacementApiKey: null, CancellationToken.None);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var root = document.RootElement;
        root.GetProperty("Future").GetProperty("Keep").GetInt32().Should().Be(42);
        root.GetProperty("Safety").GetProperty("AllowArchiveWrite").GetBoolean().Should().BeTrue();
        var content = root.GetProperty("FileContent");
        content.GetProperty("MaxInputFileBytes").GetInt64().Should().Be(2_000_000);
        content.GetProperty("MaxArchiveEntries").GetInt32().Should().Be(123);
        content.GetProperty("MaxArchiveEntryBytes").GetInt64().Should().Be(2_000_000);
        content.GetProperty("MaxArchiveTotalBytes").GetInt64().Should().Be(4_000_000);
        content.GetProperty("MaxArchiveCompressionRatio").GetInt32().Should().Be(25);
        content.GetProperty("MaxListedEntries").GetInt32().Should().Be(100);
        content.GetProperty("MaxDocumentCharacters").GetInt32().Should().Be(12_000);
        content.GetProperty("MaxSpreadsheetRows").GetInt32().Should().Be(200);
        content.GetProperty("MaxSpreadsheetColumns").GetInt32().Should().Be(20);
        content.GetProperty("MaxSpreadsheetCells").GetInt32().Should().Be(2_000);
        content.GetProperty("MaxPdfPages").GetInt32().Should().Be(25);
        configuration["Safety:AllowArchiveWrite"].Should().Be("True");
        configuration["FileContent:MaxPdfPages"].Should().Be("25");
        Directory.GetFiles(_directoryPath, "*.tmp").Should().BeEmpty();
    }

    /// <summary>Verifies invalid Stage 22 bounds never mutate the settings file.</summary>
    [Fact]
    public async Task InvalidFileContentSettingsAreRejectedBeforePersistence()
    {
        Directory.CreateDirectory(_directoryPath);
        var settingsPath = Path.Combine(_directoryPath, "settings.json");
        const string original = """{"Future":{"Keep":true}}""";
        await File.WriteAllTextAsync(settingsPath, original);
        var configuration = new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false, reloadOnChange: false).Build();
        var service = new UserSettingsService(configuration, Mock.Of<ISecretStore>(), settingsPath);
        var invalid = ValidSettings() with { FileContentMaxPdfPages = 0 };

        var action = () => service.SaveAsync(invalid, replacementApiKey: null, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllTextAsync(settingsPath)).Should().Be(original);
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
            AppThemeCatalog.DarkName,
            0.2,
            1.0,
            8192);
}
