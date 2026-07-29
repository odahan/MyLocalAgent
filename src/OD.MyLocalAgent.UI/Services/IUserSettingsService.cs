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

    /// <summary>
    /// Saves Web Search selection and endpoint while preserving unrelated settings, and
    /// optionally replaces the DPAPI-protected Web Search API key.
    /// </summary>
    /// <param name="provider">The selected provider, or an empty value when disabled.</param>
    /// <param name="endpoint">The absolute provider endpoint.</param>
    /// <param name="replacementApiKey">The optional replacement API key.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    Task SaveWebSearchAsync(
        string provider,
        string endpoint,
        string? replacementApiKey,
        CancellationToken cancellationToken);
}
