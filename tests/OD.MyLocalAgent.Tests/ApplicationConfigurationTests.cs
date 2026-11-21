using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies first-run and upgrade configuration for deployments with embedded defaults.</summary>
public sealed class ApplicationConfigurationTests
{
    /// <summary>The UI and runner can resolve their defaults without any local JSON files.</summary>
    [Fact]
    public void LoadsEmbeddedDefaultsWithoutSidecarFiles()
    {
        var directory = Directory.CreateTempSubdirectory("my-local-agent-settings-");
        try
        {
            using var configuration = new ConfigurationManager();
            HostApplicationBuilderExtensions.AddApplicationConfiguration(
                configuration, directory.FullName, Path.Combine(directory.FullName, "settings.json"));

            configuration["AI:Provider"].Should().Be("Ollama");
            configuration["FileSystem:RootPath"].Should().NotBeNullOrWhiteSpace();
            configuration["WebSearch:PreferredProvider"].Should().Be("Tavily");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Existing installation settings still apply and user settings take precedence.</summary>
    [Fact]
    public void PreservesLegacyAndUserOverrides()
    {
        var directory = Directory.CreateTempSubdirectory("my-local-agent-settings-");
        try
        {
            var userSettingsPath = Path.Combine(directory.FullName, "settings.json");
            File.WriteAllText(Path.Combine(directory.FullName, "appsettings.json"),
                """{ "Ollama": { "Model": "legacy-model", "TimeoutSeconds": 150 } }""");
            File.WriteAllText(userSettingsPath, """{ "Ollama": { "Model": "user-model" } }""");

            using var configuration = new ConfigurationManager();
            HostApplicationBuilderExtensions.AddApplicationConfiguration(configuration, directory.FullName, userSettingsPath);

            configuration["Ollama:Model"].Should().Be("user-model");
            configuration["Ollama:TimeoutSeconds"].Should().Be("150");
            configuration["AI:Provider"].Should().Be("Ollama");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
