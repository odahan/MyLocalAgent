using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;
using Serilog.Events;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Persists editable application settings while keeping sensitive values in the protected secret store.
/// </summary>
internal sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            WriteIndented = true,
        };

    private readonly IConfiguration _configuration;
    private readonly ISecretStore _secretStore;
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public UserSettingsService(
        IConfiguration configuration,
        ISecretStore secretStore)
        : this(configuration, secretStore, LocalAppPaths.UserSettingsPath)
    {
    }

    internal UserSettingsService(
        IConfiguration configuration,
        ISecretStore secretStore,
        string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _configuration = configuration;
        _secretStore = secretStore;
        _settingsPath = settingsPath;
    }

    public async Task SaveAsync(
        UserSettingsData settings,
        string? replacementApiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings with
        {
            Decision = settings.Decision ?? (DecisionOptions.HasCanonicalSection(_configuration)
                ? DecisionOptions.Resolve(_configuration)
                : settings.GmailDecision is { } legacy ? DecisionOptions.FromLegacy(legacy) : DecisionOptions.Resolve(_configuration)),
        };
        Validate(settings);

        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var legacySafetyKey in document
                         .Select(static property => property.Key)
                         .Where(static key => string.Equals(key, "Safety", StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                _ = document.Remove(legacySafetyKey);
            }

            foreach (var section in CreateDocument(
                         settings,
                         _configuration["Google:ClientId"] ?? string.Empty,
                         _configuration["Google:ClientSecret"] ?? string.Empty))
            {
                if (string.Equals(
                        section.Key,
                        AIOptions.SectionName,
                        StringComparison.Ordinal))
                {
                    var aiSection = document[AIOptions.SectionName] as JsonObject
                        ?? new JsonObject();
                    foreach (var legacyPromptKey in aiSection
                                 .Select(static property => property.Key)
                                 .Where(static key => string.Equals(
                                     key,
                                     nameof(AIOptions.GlobalSystemPrompt),
                                     StringComparison.OrdinalIgnoreCase))
                                 .ToArray())
                    {
                        _ = aiSection.Remove(legacyPromptKey);
                    }

                    var savedAI = JsonSerializer.SerializeToNode(
                        section.Value,
                        SerializerOptions)!.AsObject();
                    foreach (var property in savedAI)
                    {
                        aiSection[property.Key] = property.Value?.DeepClone();
                    }

                    document[AIOptions.SectionName] = aiSection;
                }
                else
                {
                    var saved = JsonSerializer.SerializeToNode(
                        section.Value,
                        SerializerOptions);
                    if (section.Key == DecisionOptions.SectionName && document[section.Key] is JsonObject previous && saved is JsonObject updated)
                    {
                        foreach (var property in updated) previous[property.Key] = property.Value?.DeepClone();
                    }
                    else document[section.Key] = saved;
                }
            }

            await WriteDocumentAsync(document, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(replacementApiKey))
            {
                var apiKey = replacementApiKey.Trim();
                await _secretStore
                    .SetAsync(
                        SecretKeys.OpenAIApiKey,
                        apiKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                _configuration[SecretKeys.OpenAIApiKey] = apiKey;
            }

            ReloadConfiguration();
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task SaveWebSearchAsync(
        string provider,
        string endpoint,
        string? replacementApiKey,
        CancellationToken cancellationToken)
    {
        ValidateWebSearch(provider, endpoint);
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken)
                .ConfigureAwait(false);
            var webSearch = document[WebSearchOptions.SectionName] as JsonObject
                ?? new JsonObject();
            webSearch[nameof(WebSearchOptions.Provider)] = provider;
            webSearch[nameof(WebSearchOptions.Endpoint)] = endpoint.Trim();
            document[WebSearchOptions.SectionName] = webSearch;
            await WriteDocumentAsync(document, cancellationToken)
                .ConfigureAwait(false);

            _configuration[$"{WebSearchOptions.SectionName}:Provider"] = provider;
            _configuration[$"{WebSearchOptions.SectionName}:Endpoint"] =
                endpoint.Trim();
            if (!string.IsNullOrWhiteSpace(replacementApiKey))
            {
                var apiKey = replacementApiKey.Trim();
                await _secretStore
                    .SetAsync(
                        SecretKeys.WebSearchApiKey,
                        apiKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                _configuration[SecretKeys.WebSearchApiKey] = apiKey;
            }

            ReloadConfiguration();
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task<JsonObject> ReadDocumentAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return new JsonObject();
        }

        await using var stream = new FileStream(
            _settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var node = await JsonNode
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return node as JsonObject
            ?? throw new InvalidOperationException(
                "The user settings file must contain a JSON object.");
    }

    private async Task WriteDocumentAsync(
        JsonObject document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException(
                "The user settings path has no parent directory.");
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

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void ReloadConfiguration()
    {
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
            [GmailDecisionOptions.SectionName] = CreateMailDecisionDocument(settings.GmailDecision ?? new()),
            [DecisionOptions.SectionName] = settings.Decision!,
            ["Appearance"] = new Dictionary<string, object>
            {
                ["Theme"] = settings.Theme,
            },
            ["AI"] = new Dictionary<string, object>
            {
                ["Provider"] = settings.Provider,
                ["GenerationDefaults"] = new Dictionary<string, object?>
                {
                    ["Temperature"] = settings.GenerationTemperature,
                    ["TopP"] = settings.GenerationTopP,
                    ["ContextWindowTokens"] = settings.ContextWindowTokens,
                    ["ReasoningEnabled"] = settings.GenerationReasoningEnabled,
                    ["ReasoningEffort"] = settings.GenerationReasoningEffort,
                    ["InternalThinking"] = settings.InternalThinking ?? new InternalThinkingOptions(),
                },
                ["AgentSystemPrompt"] = settings.AgentSystemPrompt.Trim(),
                ["DirectChatSystemPrompt"] = settings.DirectChatSystemPrompt.Trim(),
                ["PromptStepSystemPrompt"] = settings.PromptStepSystemPrompt.Trim(),
            },
            ["LlmExecution"] = new Dictionary<string, object>
            {
                ["MaxConcurrentRequests"] = settings.LlmMaxConcurrentRequests,
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
                ["SupportsImageInput"] = settings.OpenAISupportsImageInput,
                ["SupportsImageOutput"] = settings.OpenAISupportsImageOutput,
                ["SupportsAudioOutput"] = settings.OpenAISupportsAudioOutput,
                ["ImageModel"] = settings.OpenAIImageModel,
                ["AudioModel"] = settings.OpenAIAudioModel,
                ["AudioVoice"] = settings.OpenAIAudioVoice,
            },
            ["Knowledge"] = CreateKnowledgeDocument(settings.Knowledge ?? new KnowledgeOptions()),
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
            ["SQLite"] = new Dictionary<string, object>
            {
                ["DefaultMaxRows"] = settings.SQLiteDefaultMaxRows,
                ["MaxRows"] = settings.SQLiteMaxRows,
                ["MaxResultCharacters"] = settings.SQLiteMaxResultCharacters,
                ["MaxBlobBytes"] = settings.SQLiteMaxBlobBytes,
                ["MaxSqlCharacters"] = settings.SQLiteMaxSqlCharacters,
                ["CommandTimeoutSeconds"] = settings.SQLiteCommandTimeoutSeconds,
                ["GuardrailsEnabledByDefault"] = settings.SQLiteGuardrailsEnabledByDefault,
            },
            ["FileContent"] = new Dictionary<string, object>
            {
                ["MaxInputFileBytes"] = settings.FileContentMaxInputFileBytes,
                ["MaxArchiveEntries"] = settings.FileContentMaxArchiveEntries,
                ["MaxArchiveEntryBytes"] = settings.FileContentMaxArchiveEntryBytes,
                ["MaxArchiveTotalBytes"] = settings.FileContentMaxArchiveTotalBytes,
                ["MaxArchiveCompressionRatio"] = settings.FileContentMaxArchiveCompressionRatio,
                ["MaxListedEntries"] = settings.FileContentMaxListedEntries,
                ["MaxDocumentCharacters"] = settings.FileContentMaxDocumentCharacters,
                ["MaxSpreadsheetRows"] = settings.FileContentMaxSpreadsheetRows,
                ["MaxSpreadsheetColumns"] = settings.FileContentMaxSpreadsheetColumns,
                ["MaxSpreadsheetCells"] = settings.FileContentMaxSpreadsheetCells,
                ["MaxPdfPages"] = settings.FileContentMaxPdfPages,
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

    private static Dictionary<string, object> CreateKnowledgeDocument(KnowledgeOptions options) =>
        new(StringComparer.Ordinal)
        {
            ["EmbeddingProvider"] = options.EmbeddingProvider,
            ["EmbeddingModel"] = options.EmbeddingModel,
            ["EmbeddingBatchSize"] = options.EmbeddingBatchSize,
            ["EmbeddingTimeoutSeconds"] = options.EmbeddingTimeoutSeconds,
            ["TargetChunkTokens"] = options.TargetChunkTokens,
            ["MaximumChunkTokens"] = options.MaximumChunkTokens,
            ["OverlapChunkTokens"] = options.OverlapChunkTokens,
            ["DefaultMaxResults"] = options.DefaultMaxResults,
            ["MaximumResults"] = options.MaximumResults,
            ["DefaultMaximumResultTokens"] = options.DefaultMaximumResultTokens,
            ["MaximumResultTokens"] = options.MaximumResultTokens,
            ["MinimumSimilarity"] = options.MinimumSimilarity,
            ["MaximumImportBytes"] = options.MaximumImportBytes,
            ["MaximumExtractedCharacters"] = options.MaximumExtractedCharacters,
            ["MaximumChunksPerDocument"] = options.MaximumChunksPerDocument,
            ["MaximumQueryCharacters"] = options.MaximumQueryCharacters,
            ["MaximumExcerptCharacters"] = options.MaximumExcerptCharacters,
            ["MaximumToolResultCharacters"] = options.MaximumToolResultCharacters,
            ["SemanticCandidatePoolSize"] = options.SemanticCandidatePoolSize,
            ["LexicalCandidatePoolSize"] = options.LexicalCandidatePoolSize,
            ["RrfRankConstant"] = options.RrfRankConstant,
            ["MmrLambda"] = options.MmrLambda,
            ["MaximumContextNeighborsEachSide"] = options.MaximumContextNeighborsEachSide,
            ["MaximumLexicalQueryTerms"] = options.MaximumLexicalQueryTerms,
            ["MaximumLexicalTermCharacters"] = options.MaximumLexicalTermCharacters,
        };

    private static Dictionary<string, JsonElement> CreateMailDecisionDocument(GmailDecisionOptions mail)
    {
        var nativeNames = typeof(DecisionOptions).GetProperties()
            .Where(property => property.Name != nameof(DecisionOptions.Enabled))
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        return JsonSerializer.SerializeToElement(mail).EnumerateObject()
            .Where(property => !nativeNames.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    private static void Validate(UserSettingsData settings)
    {
        var native = settings.Decision ?? DecisionOptions.FromLegacy(settings.GmailDecision ?? new());
        var decision = native.ApplyTo(settings.GmailDecision ?? new());
        if (!DecisionOptions.IsValid(native) || !GmailDecisionOptions.IsValidMail(decision) ||
            (native.Engine == "NativeDecision" && (decision.Enabled || native.Enabled) && !LocalDecisionEndpoint.IsValid(native.DecisionEndpoint ?? settings.OllamaEndpoint)))
            throw new InvalidOperationException("Local decision model settings require bounded policies and loopback endpoints.");
        if (!KnowledgeOptions.IsValid(settings.Knowledge ?? new KnowledgeOptions()))
        {
            throw new InvalidOperationException("Knowledge embedding settings are invalid.");
        }

        if (!AppThemeCatalog.TryParse(settings.Theme, out _))
        {
            throw new InvalidOperationException(
                "Select a supported application theme.");
        }

        if (settings.Provider is not AIOptions.OllamaProvider and not AIOptions.OpenAIProvider)
        {
            throw new InvalidOperationException("Select a supported AI provider.");
        }

        if (settings.LlmMaxConcurrentRequests is < 1 or > 8)
        {
            throw new InvalidOperationException(
                "LLM maximum concurrent requests must be between 1 and 8.");
        }

        var generationError = GenerationPolicyValidation.ValidateDefaults(
            new AIGenerationDefaults
            {
                Temperature = settings.GenerationTemperature,
                TopP = settings.GenerationTopP,
                ContextWindowTokens = settings.ContextWindowTokens,
                ReasoningEnabled = settings.GenerationReasoningEnabled,
                ReasoningEffort = settings.GenerationReasoningEffort,
                InternalThinking = settings.InternalThinking ?? new InternalThinkingOptions(),
            });
        if (generationError is not null)
        {
            throw new InvalidOperationException(generationError);
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
        if (settings.OpenAISupportsImageOutput && string.IsNullOrWhiteSpace(settings.OpenAIImageModel))
        {
            throw new InvalidOperationException(
                "An image model is required when Direct Chat image output is enabled.");
        }

        if (settings.OpenAISupportsAudioOutput &&
            (string.IsNullOrWhiteSpace(settings.OpenAIAudioModel) ||
             string.IsNullOrWhiteSpace(settings.OpenAIAudioVoice)))
        {
            throw new InvalidOperationException(
                "An audio model and voice are required when Direct Chat audio output is enabled.");
        }
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

        if (!SQLiteOptions.IsValid(new SQLiteOptions
            {
                DefaultMaxRows = settings.SQLiteDefaultMaxRows,
                MaxRows = settings.SQLiteMaxRows,
                MaxResultCharacters = settings.SQLiteMaxResultCharacters,
                MaxBlobBytes = settings.SQLiteMaxBlobBytes,
                MaxSqlCharacters = settings.SQLiteMaxSqlCharacters,
                CommandTimeoutSeconds = settings.SQLiteCommandTimeoutSeconds,
                GuardrailsEnabledByDefault = settings.SQLiteGuardrailsEnabledByDefault,
            }))
        {
            throw new InvalidOperationException("SQLite limits or timeout are invalid.");
        }

        if (!FileContentOptions.IsValid(new FileContentOptions
            {
                MaxInputFileBytes = settings.FileContentMaxInputFileBytes,
                MaxArchiveEntries = settings.FileContentMaxArchiveEntries,
                MaxArchiveEntryBytes = settings.FileContentMaxArchiveEntryBytes,
                MaxArchiveTotalBytes = settings.FileContentMaxArchiveTotalBytes,
                MaxArchiveCompressionRatio = settings.FileContentMaxArchiveCompressionRatio,
                MaxListedEntries = settings.FileContentMaxListedEntries,
                MaxDocumentCharacters = settings.FileContentMaxDocumentCharacters,
                MaxSpreadsheetRows = settings.FileContentMaxSpreadsheetRows,
                MaxSpreadsheetColumns = settings.FileContentMaxSpreadsheetColumns,
                MaxSpreadsheetCells = settings.FileContentMaxSpreadsheetCells,
                MaxPdfPages = settings.FileContentMaxPdfPages,
            }))
        {
            throw new InvalidOperationException("File content limits are invalid.");
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

    private static void ValidateWebSearch(
        string provider,
        string endpoint)
    {
        if (provider is not ("" or WebSearchOptions.OllamaProvider))
        {
            throw new InvalidOperationException(
                "Select a supported Web Search provider.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            endpointUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Web Search endpoint must be an absolute HTTP or HTTPS URL.");
        }
    }
}
