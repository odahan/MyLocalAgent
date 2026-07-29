using System.ComponentModel;
using Microsoft.Extensions.Logging;
using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes compact-JSON Dropbox connector operations.
/// </summary>
public sealed class DropboxTool : IDropboxTool
{
    private readonly IDropboxConnector _connector;
    private readonly IPathSandbox _sandbox;
    private readonly ILogger<DropboxTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DropboxTool"/> class.
    /// </summary>
    public DropboxTool(
        IDropboxConnector connector,
        IPathSandbox sandbox,
        ILogger<DropboxTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connector = connector;
        _sandbox = sandbox;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Description("Uploads a sandboxed local file to Dropbox.")]
    public Task<string> UploadAsync(
        [Description("Relative local source path.")] string localPath,
        [Description("Dropbox destination path.")] string dropboxPath,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "dropbox.upload",
            new { localPath, dropboxPath },
            async () =>
            {
                ValidateDropboxPath(dropboxPath);
                var absoluteLocalPath = _sandbox.Resolve(localPath);
                var item = await _connector.UploadAsync(
                    absoluteLocalPath,
                    dropboxPath,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(item);
            });

    /// <inheritdoc />
    [Description("Downloads a Dropbox file into the local file-system sandbox.")]
    public Task<string> DownloadAsync(
        [Description("Dropbox source path.")] string dropboxPath,
        [Description("Relative local destination path.")] string localPath,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "dropbox.download",
            new { dropboxPath, localPath },
            async () =>
            {
                ValidateDropboxPath(dropboxPath);
                var absoluteLocalPath = _sandbox.Resolve(localPath);
                var parent = Path.GetDirectoryName(absoluteLocalPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                var item = await _connector.DownloadAsync(
                    dropboxPath,
                    absoluteLocalPath,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(item);
            });

    /// <inheritdoc />
    [Description("Lists the immediate children of a Dropbox folder.")]
    public Task<string> ListAsync(
        [Description("Dropbox folder path.")] string folderPath,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "dropbox.list",
            new { folderPath },
            async () =>
            {
                ArgumentNullException.ThrowIfNull(folderPath);
                var items = await _connector.ListAsync(folderPath, cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(items);
            });

    private static void ValidateDropboxPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("A Dropbox path must start with '/'.", nameof(path));
        }
    }
}
