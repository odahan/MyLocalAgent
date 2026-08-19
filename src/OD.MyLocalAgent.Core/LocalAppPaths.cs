namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides stable per-user configuration and credential paths.
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
