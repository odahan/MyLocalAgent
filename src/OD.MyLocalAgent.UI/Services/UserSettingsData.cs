using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Carries the validated, non-secret values written to the per-user settings file.
/// </summary>
/// <param name="Provider">The active provider.</param>
/// <param name="OllamaEndpoint">The Ollama endpoint.</param>
/// <param name="OllamaModel">The Ollama model.</param>
/// <param name="OllamaTimeoutSeconds">The Ollama timeout.</param>
/// <param name="OpenAIEndpoint">The OpenAI-compatible endpoint.</param>
/// <param name="OpenAIModel">The OpenAI-compatible model.</param>
/// <param name="OpenAITimeoutSeconds">The OpenAI-compatible timeout.</param>
/// <param name="FileSystemRootPath">The file-system sandbox root.</param>
/// <param name="HttpTimeoutSeconds">The HTTP timeout.</param>
/// <param name="HttpUserAgent">The HTTP user agent.</param>
/// <param name="AllowedHosts">The optional HTTP host allowlist.</param>
/// <param name="TaskFolder">The Windows Task Scheduler folder.</param>
/// <param name="RunWhenLoggedOff">Whether S4U logon is enabled.</param>
/// <param name="RunnerPath">The optional runner path.</param>
/// <param name="GuardrailsScreeningEnabled">Whether LLM guardrails processing is enabled.</param>
/// <param name="GuardrailsCleanThreshold">The clean-delivery threshold.</param>
/// <param name="GuardrailsCautionThreshold">The caution-delivery threshold.</param>
/// <param name="GuardrailsMaxContentChars">The single-call content maximum.</param>
/// <param name="GuardrailsSummarizeOversizedContent">Whether oversized reduction is enabled.</param>
/// <param name="GuardrailsMaxSummaryChars">The generated-summary maximum.</param>
/// <param name="GuardrailsMaxSummarizationSourceChars">The complete-source reduction maximum.</param>
/// <param name="GuardrailsTimeoutSeconds">The per-call guardrail timeout.</param>
/// <param name="GuardrailsCacheResults">Whether in-memory guardrail caching is enabled.</param>
/// <param name="GuardrailsMaxGmailMessagesPerOperation">The Gmail processing limit.</param>
/// <param name="LoggingMinimumLevel">The Serilog minimum level.</param>
/// <param name="Theme">The application color theme.</param>
/// <param name="GenerationTemperature">The global default sampling temperature.</param>
/// <param name="GenerationTopP">The global default nucleus-sampling value.</param>
/// <param name="ContextWindowTokens">The declared global context-window budget.</param>
/// <param name="SQLiteDefaultMaxRows">The default SQLite row limit.</param>
/// <param name="SQLiteMaxRows">The maximum SQLite row limit.</param>
/// <param name="SQLiteMaxResultCharacters">The maximum serialized SQLite result size.</param>
/// <param name="SQLiteMaxBlobBytes">The maximum delivered SQLite BLOB size.</param>
/// <param name="SQLiteMaxSqlCharacters">The maximum SQLite SQL size.</param>
/// <param name="SQLiteCommandTimeoutSeconds">The SQLite command timeout.</param>
/// <param name="SQLiteGuardrailsEnabledByDefault">The SQLite Guardrails default for new tasks.</param>
/// <param name="AgentSystemPrompt">The optional agentic-task instructions.</param>
/// <param name="DirectChatSystemPrompt">The optional Direct Chat instructions.</param>
/// <param name="PromptStepSystemPrompt">The optional workflow prompt-step instructions.</param>
/// <param name="FileContentMaxInputFileBytes">The maximum parser input size.</param>
/// <param name="FileContentMaxArchiveEntries">The archive entry ceiling.</param>
/// <param name="FileContentMaxArchiveEntryBytes">The per-entry extraction ceiling.</param>
/// <param name="FileContentMaxArchiveTotalBytes">The total extraction ceiling.</param>
/// <param name="FileContentMaxArchiveCompressionRatio">The archive compression-ratio ceiling.</param>
/// <param name="FileContentMaxListedEntries">The archive list ceiling.</param>
/// <param name="FileContentMaxDocumentCharacters">The document text ceiling.</param>
/// <param name="FileContentMaxSpreadsheetRows">The spreadsheet row ceiling.</param>
/// <param name="FileContentMaxSpreadsheetColumns">The spreadsheet column ceiling.</param>
/// <param name="FileContentMaxSpreadsheetCells">The spreadsheet cell ceiling.</param>
/// <param name="FileContentMaxPdfPages">The PDF page ceiling.</param>
/// <param name="OpenAISupportsImageInput">Whether the exact endpoint declares image input.</param>
/// <param name="OpenAISupportsImageOutput">Whether the exact endpoint declares image output.</param>
/// <param name="OpenAISupportsAudioOutput">Whether the exact endpoint declares audio output.</param>
/// <param name="OpenAIImageModel">The dedicated image-generation model.</param>
/// <param name="OpenAIAudioModel">The dedicated speech-generation model.</param>
/// <param name="OpenAIAudioVoice">The dedicated speech voice.</param>
/// <param name="Knowledge">The independent Knowledge embedding and retrieval settings.</param>
public sealed record UserSettingsData(
    string Provider,
    string OllamaEndpoint,
    string OllamaModel,
    int OllamaTimeoutSeconds,
    string OpenAIEndpoint,
    string OpenAIModel,
    int OpenAITimeoutSeconds,
    string FileSystemRootPath,
    int HttpTimeoutSeconds,
    string HttpUserAgent,
    IReadOnlyList<string> AllowedHosts,
    string TaskFolder,
    bool RunWhenLoggedOff,
    string RunnerPath,
    bool GuardrailsScreeningEnabled,
    int GuardrailsCleanThreshold,
    int GuardrailsCautionThreshold,
    int GuardrailsMaxContentChars,
    bool GuardrailsSummarizeOversizedContent,
    int GuardrailsMaxSummaryChars,
    int GuardrailsMaxSummarizationSourceChars,
    int GuardrailsTimeoutSeconds,
    bool GuardrailsCacheResults,
    int GuardrailsMaxGmailMessagesPerOperation,
    string LoggingMinimumLevel,
    string Theme,
    double GenerationTemperature,
    double GenerationTopP,
    int ContextWindowTokens,
    int SQLiteDefaultMaxRows = 100,
    int SQLiteMaxRows = 1_000,
    int SQLiteMaxResultCharacters = 64_000,
    int SQLiteMaxBlobBytes = 4_096,
    int SQLiteMaxSqlCharacters = 16_000,
    int SQLiteCommandTimeoutSeconds = 30,
    bool SQLiteGuardrailsEnabledByDefault = true,
    string AgentSystemPrompt = "",
    string DirectChatSystemPrompt = "",
    string PromptStepSystemPrompt = "",
    long FileContentMaxInputFileBytes = 104857600,
    int FileContentMaxArchiveEntries = 10000,
    long FileContentMaxArchiveEntryBytes = 262144000,
    long FileContentMaxArchiveTotalBytes = 1073741824,
    int FileContentMaxArchiveCompressionRatio = 200,
    int FileContentMaxListedEntries = 1000,
    int FileContentMaxDocumentCharacters = 64000,
    int FileContentMaxSpreadsheetRows = 1000,
    int FileContentMaxSpreadsheetColumns = 256,
    int FileContentMaxSpreadsheetCells = 25000,
    int FileContentMaxPdfPages = 100,
    bool OpenAISupportsImageInput = false,
    bool OpenAISupportsImageOutput = false,
    bool OpenAISupportsAudioOutput = false,
    string OpenAIImageModel = "gpt-image-2",
    string OpenAIAudioModel = "gpt-4o-mini-tts",
    string OpenAIAudioVoice = "coral",
    KnowledgeOptions? Knowledge = null);
