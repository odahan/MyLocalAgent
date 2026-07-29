using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;
using Serilog.Events;

namespace OD.MyLocalAgent.UI.Services;

internal sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
        };

    private readonly IConfiguration _configuration;
    private readonly ISecretStore _secretStore;

    public UserSettingsService(
        IConfiguration configuration,
        ISecretStore secretStore)
    {
        _configuration = configuration;
        _secretStore = secretStore;
    }

    public async Task SaveAsync(
        UserSettingsData settings,
        string? replacementApiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);

        var document = CreateDocument(
            settings,
            _configuration["Google:ClientId"] ?? string.Empty,
            _configuration["Google:ClientSecret"] ?? string.Empty);
        var directory = Path.GetDirectoryName(LocalAppPaths.UserSettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer
                    .SerializeAsync(
                        stream,
                        document,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(
                temporaryPath,
                LocalAppPaths.UserSettingsPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(replacementApiKey))
        {
            await _secretStore
                .SetAsync(
                    SecretKeys.OpenAIApiKey,
                    replacementApiKey.Trim(),
                    cancellationToken)
                .ConfigureAwait(false);
            _configuration[SecretKeys.OpenAIApiKey] = replacementApiKey.Trim();
        }

        if (_configuration is IConfigurationRoot configurationRoot)
        {
            configurationRoot.Reload();
        }
    }

    private static Dictionary<string, object> CreateDocument(
        UserSettingsData settings,
        string googleClientId,
        string googleClientSecret) =>
        new(StringComparer.Ordinal)
        {
            ["Appearance"] = new Dictionary<string, object>
            {
                ["Theme"] = settings.Theme,
            },
            ["AI"] = new Dictionary<string, object>
            {
                ["Provider"] = settings.Provider,
            },
            ["Ollama"] = new Dictionary<string, object>
            {
                ["Endpoint"] = settings.OllamaEndpoint,
                ["Model"] = settings.OllamaModel,
                ["TimeoutSeconds"] = settings.OllamaTimeoutSeconds,
            },
            ["OpenAI"] = new Dictionary<string, object>
            {
                ["Endpoint"] = settings.OpenAIEndpoint,
                ["Model"] = settings.OpenAIModel,
                ["TimeoutSeconds"] = settings.OpenAITimeoutSeconds,
            },
            ["Google"] = new Dictionary<string, object>
            {
                ["ClientId"] = googleClientId,
                ["ClientSecret"] = googleClientSecret,
            },
            ["FileSystem"] = new Dictionary<string, object>
            {
                ["RootPath"] = settings.FileSystemRootPath,
            },
            ["Http"] = new Dictionary<string, object>
            {
                ["TimeoutSeconds"] = settings.HttpTimeoutSeconds,
                ["UserAgent"] = settings.HttpUserAgent,
                ["AllowedHosts"] = settings.AllowedHosts,
            },
            ["Scheduler"] = new Dictionary<string, object>
            {
                ["TaskFolder"] = settings.TaskFolder,
                ["RunWhenLoggedOff"] = settings.RunWhenLoggedOff,
                ["RunnerPath"] = settings.RunnerPath,
            },
            ["Safety"] = new Dictionary<string, object>
            {
                ["AllowSendEmail"] = settings.AllowSendEmail,
                ["AllowFileDelete"] = settings.AllowFileDelete,
                ["AllowHttpPost"] = settings.AllowHttpPost,
            },
            ["Guardrails"] = new Dictionary<string, object>
            {
                ["ScreeningEnabled"] = settings.GuardrailsScreeningEnabled,
                ["CleanThreshold"] = settings.GuardrailsCleanThreshold,
                ["CautionThreshold"] = settings.GuardrailsCautionThreshold,
                ["MaxContentChars"] = settings.GuardrailsMaxContentChars,
                ["SummarizeOversizedContent"] =
                    settings.GuardrailsSummarizeOversizedContent,
                ["MaxSummaryChars"] = settings.GuardrailsMaxSummaryChars,
                ["MaxSummarizationSourceChars"] =
                    settings.GuardrailsMaxSummarizationSourceChars,
                ["TimeoutSeconds"] = settings.GuardrailsTimeoutSeconds,
                ["CacheResults"] = settings.GuardrailsCacheResults,
                ["MaxGmailMessagesPerOperation"] =
                    settings.GuardrailsMaxGmailMessagesPerOperation,
            },
            ["Logging"] = new Dictionary<string, object>
            {
                ["MinimumLevel"] = settings.LoggingMinimumLevel,
            },
        };

    private static void Validate(UserSettingsData settings)
    {
        if (!AppThemeCatalog.TryParse(settings.Theme, out _))
        {
            throw new InvalidOperationException(
                "Select a supported application theme.");
        }

        if (settings.Provider is not AIOptions.OllamaProvider and not AIOptions.OpenAIProvider)
        {
            throw new InvalidOperationException("Select a supported AI provider.");
        }

        ValidateProvider(
            "Ollama",
            settings.OllamaEndpoint,
            settings.OllamaModel,
            settings.OllamaTimeoutSeconds);
        ValidateProvider(
            "OpenAI-compatible",
            settings.OpenAIEndpoint,
            settings.OpenAIModel,
            settings.OpenAITimeoutSeconds);
        if (string.IsNullOrWhiteSpace(settings.FileSystemRootPath) ||
            !Path.IsPathRooted(settings.FileSystemRootPath))
        {
            throw new InvalidOperationException(
                "File-system root path must be an absolute path.");
        }

        if (settings.HttpTimeoutSeconds <= 0 ||
            string.IsNullOrWhiteSpace(settings.HttpUserAgent))
        {
            throw new InvalidOperationException(
                "HTTP timeout must be positive and the user agent is required.");
        }

        if (settings.AllowedHosts.Any(
                static host =>
                    string.IsNullOrWhiteSpace(host) ||
                    host.Contains('/') ||
                    host.Contains('\\')))
        {
            throw new InvalidOperationException(
                "Allowed HTTP hosts must be host names without paths.");
        }

        if (string.IsNullOrWhiteSpace(settings.TaskFolder))
        {
            throw new InvalidOperationException(
                "The Task Scheduler folder is required.");
        }

        if (settings.GuardrailsCautionThreshold < 0 ||
            settings.GuardrailsCautionThreshold >= settings.GuardrailsCleanThreshold ||
            settings.GuardrailsCleanThreshold > 100 ||
            settings.GuardrailsMaxContentChars <= 0 ||
            settings.GuardrailsMaxSummaryChars < 256 ||
            settings.GuardrailsMaxSummaryChars > settings.GuardrailsMaxContentChars ||
            settings.GuardrailsMaxSummarizationSourceChars <=
            settings.GuardrailsMaxContentChars ||
            settings.GuardrailsTimeoutSeconds <= 0 ||
            settings.GuardrailsMaxGmailMessagesPerOperation < 0)
        {
            throw new InvalidOperationException(
                "Guardrails thresholds, content limits, timeout, or Gmail limit are invalid.");
        }

        if (!Enum.TryParse<LogEventLevel>(
                settings.LoggingMinimumLevel,
                ignoreCase: true,
                out _))
        {
            throw new InvalidOperationException(
                "Select a valid Serilog minimum level.");
        }
    }

    private static void ValidateProvider(
        string provider,
        string endpoint,
        string model,
        int timeoutSeconds)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"{provider} endpoint must be an absolute HTTP or HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(model) || timeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"{provider} model is required and timeout must be positive.");
        }
    }
}
