namespace OD.MyLocalAgent.Core;

/// <summary>
/// Stores per-user application secrets protected by Windows DPAPI.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Gets a secret value.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The secret value, or <see langword="null"/> when absent.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a secret value.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a secret value.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns><see langword="true"/> when a value was removed.</returns>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether a secret exists.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns><see langword="true"/> when a value exists.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}
