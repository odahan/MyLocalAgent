using Google.Apis.Auth.OAuth2;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides interactive and silent Google OAuth credential acquisition.
/// </summary>
public interface IGoogleCredentialProvider
{
    /// <summary>
    /// Runs the interactive authorization-code flow and persists the resulting credential.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The authorized user credential.</returns>
    Task<UserCredential> AuthorizeInteractiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads and refreshes a cached credential without launching a browser.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The cached credential, or <see langword="null"/> when consent is required.</returns>
    Task<UserCredential?> GetCredentialSilentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Clears the cached credential and persisted Google authorization data.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken);
}
