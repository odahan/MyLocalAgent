namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Validates and persists reloadable per-user application settings.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Saves non-secret settings and optionally replaces the DPAPI-protected API key.
    /// </summary>
    Task SaveAsync(
        UserSettingsData settings,
        string? replacementApiKey,
        CancellationToken cancellationToken);
}
