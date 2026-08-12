namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides stable per-user application data paths.
/// </summary>
public static class LocalAppPaths
{
    /// <summary>
    /// Gets the application data directory.
    /// </summary>
    public static string ApplicationDataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OD.MyLocalAgent");

    /// <summary>
    /// Gets the application log directory.
    /// </summary>
    public static string LogsDirectory =>
        Path.Combine(ApplicationDataDirectory, "logs");

    /// <summary>
    /// Gets the persisted task directory.
    /// </summary>
    public static string TasksDirectory =>
        Path.Combine(ApplicationDataDirectory, "tasks");

    /// <summary>
    /// Gets the dedicated Direct Chat database path.
    /// </summary>
    public static string DirectChatDatabasePath =>
        Path.Combine(ApplicationDataDirectory, "direct-chat.db");

    /// <summary>
    /// Gets the incident JSONL path.
    /// </summary>
    public static string IncidentsPath =>
        Path.Combine(LogsDirectory, "incidents.jsonl");

    /// <summary>
    /// Gets the per-execution runner output log directory.
    /// </summary>
    public static string RunLogsDirectory =>
        Path.Combine(LogsDirectory, "runs");

    /// <summary>
    /// Gets the DPAPI-protected secret-store path.
    /// </summary>
    public static string SecretsPath =>
        Path.Combine(ApplicationDataDirectory, "secrets.dat");

    /// <summary>
    /// Gets the user settings path.
    /// </summary>
    public static string UserSettingsPath =>
        Path.Combine(ApplicationDataDirectory, "settings.json");
}
