namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides low-level Dropbox file operations.
/// </summary>
public interface IDropboxConnector
{
    /// <summary>
    /// Uploads a local file to Dropbox.
    /// </summary>
    /// <param name="localPath">The source local path.</param>
    /// <param name="dropboxPath">The destination Dropbox path.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The uploaded file metadata.</returns>
    Task<DropboxItemDto> UploadAsync(
        string localPath,
        string dropboxPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Downloads a Dropbox file to a local path.
    /// </summary>
    /// <param name="dropboxPath">The source Dropbox path.</param>
    /// <param name="localPath">The destination local path.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The downloaded file metadata.</returns>
    Task<DropboxItemDto> DownloadAsync(
        string dropboxPath,
        string localPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists all immediate children of a Dropbox folder.
    /// </summary>
    /// <param name="folderPath">The Dropbox folder path.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The normalized items.</returns>
    Task<IReadOnlyList<DropboxItemDto>> ListAsync(
        string folderPath,
        CancellationToken cancellationToken);
}
