using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
                "FutureSetting": "preserve",
                "GlobalSystemPrompt": "legacy instructions"
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
            ValidSettings() with
            {
                AgentSystemPrompt = "Agent instructions",
                DirectChatSystemPrompt = "Chat instructions",
                PromptStepSystemPrompt = "Prompt step instructions",
            },
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
        ai.GetProperty("AgentSystemPrompt").GetString()
            .Should().Be("Agent instructions");
        ai.GetProperty("DirectChatSystemPrompt").GetString()
            .Should().Be("Chat instructions");
        ai.GetProperty("PromptStepSystemPrompt").GetString()
            .Should().Be("Prompt step instructions");
        ai.TryGetProperty("GlobalSystemPrompt", out _).Should().BeFalse();
        root.TryGetProperty("Safety", out _).Should().BeFalse();
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
    /// Verifies every SQLite setting persists, legacy Safety is removed, and unrelated sections are preserved.
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
            "Secrets": { "Preserve": "unchanged" },
            "Safety": { "AllowSqliteWrite": true }
            }
            """);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false, reloadOnChange: false)
            .Build();
        var settings = ValidSettings() with
        {
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
        root.TryGetProperty("Safety", out _).Should().BeFalse();
        var sqlite = root.GetProperty("SQLite");
        sqlite.GetProperty("DefaultMaxRows").GetInt32().Should().Be(321);
        sqlite.GetProperty("MaxRows").GetInt32().Should().Be(654);
        sqlite.GetProperty("MaxResultCharacters").GetInt32().Should().Be(12_345);
        sqlite.GetProperty("MaxBlobBytes").GetInt32().Should().Be(678);
        sqlite.GetProperty("MaxSqlCharacters").GetInt32().Should().Be(9_876);
        sqlite.GetProperty("CommandTimeoutSeconds").GetInt32().Should().Be(45);
        sqlite.GetProperty("GuardrailsEnabledByDefault").GetBoolean().Should().BeFalse();
        configuration["Safety:AllowSqliteWrite"].Should().BeNull();
        configuration["SQLite:MaxResultCharacters"].Should().Be("12345");
        configuration["SQLite:GuardrailsEnabledByDefault"].Should().Be("False");
    }

    /// <summary>Verifies every Stage 22 file-content value persists atomically.</summary>
    [Fact]
    public async Task SavePersistsAllFileContentSettingsAndPreservesUnknownSections()
    {
        Directory.CreateDirectory(_directoryPath);
        var settingsPath = Path.Combine(_directoryPath, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """{"Future":{"Keep":42}}""");
        var configuration = new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false, reloadOnChange: false).Build();
        var settings = ValidSettings() with
        {
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
        root.TryGetProperty("Safety", out _).Should().BeFalse();
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

    /// <summary>Missing decision sections acquire disabled defaults; explicit settings persist without any model service.</summary>
    [Fact]
    public async Task DecisionSettingsMigrateAndPersistLocally()
    {
        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, "settings.json");
        await File.WriteAllTextAsync(path, "{}");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var service = new UserSettingsService(configuration, Mock.Of<ISecretStore>(), path);
        await service.SaveAsync(ValidSettings(), null, default);
        using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
            json.RootElement.GetProperty("GmailDecision").GetProperty("Enabled").GetBoolean().Should().BeFalse();
        var decision = new GmailDecisionOptions { Enabled = true, KeepAliveSeconds = 0, MaxArbitrationPercent = 0, FallbackModel = "small:latest" };
        await service.SaveAsync(ValidSettings() with { GmailDecision = decision, Decision = DecisionOptions.FromLegacy(decision) }, null, default);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        document.RootElement.GetProperty("Decision").GetProperty("KeepAliveSeconds").GetInt32().Should().Be(0);
        document.RootElement.GetProperty("Decision").GetProperty("Enabled").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("GmailDecision").TryGetProperty("KeepAliveSeconds", out _).Should().BeFalse();
        document.RootElement.GetProperty("GmailDecision").GetProperty("FallbackModel").GetString().Should().Be("small:latest");
        await FluentActions.Awaiting(() => service.SaveAsync(ValidSettings() with { GmailDecision = decision,
            Decision = DecisionOptions.FromLegacy(decision) with { DecisionEndpoint = "https://remote.example.test" } }, null, default))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>Legacy native values survive the first save and reload without enabling generic evaluation.</summary>
    [Fact]
    public async Task LegacyNativeMigrationPreservesEffectiveMailSettingsOnReload()
    {
        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, "settings.json");
        var legacy = new GmailDecisionOptions { Enabled = true, DecisionModel = "custom:latest", KeepAliveSeconds = 3,
            MinTopProbabilityPercent = 73.5, ArbitrationEnabled = false, BatchTimeoutSeconds = 600 };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { GmailDecision = legacy }));
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        using var provider = new ServiceCollection().AddDecisionConfiguration(configuration).BuildServiceProvider();
        var before = provider.GetRequiredService<IOptionsMonitor<GmailDecisionOptions>>().CurrentValue;
        var service = new UserSettingsService(configuration, Mock.Of<ISecretStore>(), path);
        await service.SaveAsync(ValidSettings() with { GmailDecision = before }, null, default);
        var after = provider.GetRequiredService<IOptionsMonitor<GmailDecisionOptions>>().CurrentValue;
        after.Should().BeEquivalentTo(before);
        var native = provider.GetRequiredService<IOptionsMonitor<DecisionOptions>>().CurrentValue;
        native.Enabled.Should().BeFalse();
        native.DecisionModel.Should().Be("custom:latest");
        native.MinTopProbabilityPercent.Should().Be(73.5);
        native.OperationTimeoutSeconds.Should().Be(180);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        document.RootElement.GetProperty("GmailDecision").TryGetProperty("DecisionModel", out _).Should().BeFalse();
    }

    /// <summary>An authoritative native section is preserved by callers that save only legacy mail policy.</summary>
    [Fact]
    public async Task SavingMailPolicyDoesNotReplaceCanonicalNativeSettings()
    {
        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, "settings.json");
        await File.WriteAllTextAsync(path, """{"Decision":{"Enabled":true,"DecisionModel":"canonical:latest","KeepAliveSeconds":7}}""");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var service = new UserSettingsService(configuration, Mock.Of<ISecretStore>(), path);
        await service.SaveAsync(ValidSettings() with { GmailDecision = new() { Enabled = true, DecisionModel = "legacy:latest" } }, null, default);
        var native = DecisionOptions.Resolve(configuration);
        native.Enabled.Should().BeTrue();
        native.DecisionModel.Should().Be("canonical:latest");
        native.KeepAliveSeconds.Should().Be(7);
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
    /// <summary>Central saves retain inactive native settings, historical fallback data, and unrelated configuration.</summary>
    [Fact]
    public async Task CentralSaveRetainsInactiveNativeAndUnrelatedValues()
    {
        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, "central-retention.json");
        await File.WriteAllTextAsync(path, """{"Decision":{"CustomFutureSetting":"keep"},"Unrelated":{"Value":42}}""");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var service = new UserSettingsService(configuration, Mock.Of<ISecretStore>(), path);
        var decision = new DecisionOptions { Engine = "CentralLlm", DecisionModel = "retained-native", MaxInputTokens = 123,
            DecisionColdTimeoutSeconds = -1, MinTopProbabilityPercent = 87 };
        await service.SaveAsync(ValidSettings() with { Decision = decision, GmailDecision = new() { FallbackModel = "historical-fallback" } }, null, default);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var saved = document.RootElement.GetProperty("Decision");
        saved.GetProperty("Engine").GetString().Should().Be("CentralLlm");
        saved.GetProperty("DecisionModel").GetString().Should().Be("retained-native");
        saved.GetProperty("MaxInputTokens").GetInt32().Should().Be(123);
        saved.GetProperty("Enabled").GetBoolean().Should().BeFalse();
        saved.GetProperty("CustomFutureSetting").GetString().Should().Be("keep");
        document.RootElement.GetProperty("Unrelated").GetProperty("Value").GetInt32().Should().Be(42);
        document.RootElement.GetProperty("GmailDecision").GetProperty("FallbackModel").GetString().Should().Be("historical-fallback");
    }
}
