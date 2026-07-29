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
/// <param name="AllowSendEmail">Whether email sending is enabled.</param>
/// <param name="AllowFileDelete">Whether file deletion is enabled.</param>
/// <param name="AllowHttpPost">Whether HTTP POST is enabled.</param>
/// <param name="LoggingMinimumLevel">The Serilog minimum level.</param>
/// <param name="Theme">The application color theme.</param>
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
    bool AllowSendEmail,
    bool AllowFileDelete,
    bool AllowHttpPost,
    string LoggingMinimumLevel,
    string Theme);
