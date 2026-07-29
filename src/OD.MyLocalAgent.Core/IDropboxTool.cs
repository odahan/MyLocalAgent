namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides Dropbox operations to agents and workflows.
/// </summary>
public interface IDropboxTool
{
    /// <summary>
    /// Uploads a sandboxed local file.
    /// </summary>
    Task<string> UploadAsync(
        string localPath,
        string dropboxPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Downloads a Dropbox file into the local sandbox.
    /// </summary>
    Task<string> DownloadAsync(
        string dropboxPath,
        string localPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists a Dropbox folder.
    /// </summary>
    Task<string> ListAsync(string folderPath, CancellationToken cancellationToken);
}
