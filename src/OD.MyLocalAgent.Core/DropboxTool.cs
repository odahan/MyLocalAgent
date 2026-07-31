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
    private readonly ToolExecutionContext _executionContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DropboxTool"/> class.
    /// </summary>
    public DropboxTool(
        IDropboxConnector connector,
        IPathSandbox sandbox,
        ILogger<DropboxTool> logger,
        TimeProvider timeProvider,
        ToolExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(executionContext);
        _connector = connector;
        _sandbox = sandbox;
        _logger = logger;
        _timeProvider = timeProvider;
        _executionContext = executionContext;
    }

    /// <inheritdoc />
    [Description("Uploads an authorized local file to Dropbox.")]
    public Task<string> UploadAsync(
        [Description("Relative global-root or task-authorized absolute local source path.")] string localPath,
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
                var absoluteLocalPath = _sandbox.Resolve(
                    localPath,
                    FileSystemAccessMode.ReadOnly,
                    _executionContext);
                var item = await _connector.UploadAsync(
                    absoluteLocalPath,
                    dropboxPath,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(item);
            },
            SummarizeDropboxResult);

    /// <inheritdoc />
    [Description("Downloads a Dropbox file into an authorized local path.")]
    public Task<string> DownloadAsync(
        [Description("Dropbox source path.")] string dropboxPath,
        [Description("Relative global-root or task-authorized absolute local destination path.")] string localPath,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "dropbox.download",
            new { dropboxPath, localPath },
            async () =>
            {
                ValidateDropboxPath(dropboxPath);
                var absoluteLocalPath = _sandbox.Resolve(
                    localPath,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
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
            },
            SummarizeDropboxResult);

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
            },
            SummarizeDropboxResult);

    private static string? SummarizeDropboxResult(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            return FormatDropboxItem(root);
        }

        if (root.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return null;
        }

        var items = root.EnumerateArray().ToArray();
        var details = items
            .Take(10)
            .Select(FormatDropboxItem)
            .Where(static value => !string.IsNullOrWhiteSpace(value));
        var suffix = items.Length > 10 ? " …" : string.Empty;
        return $"Returned {items.Length} Dropbox item(s): {string.Join(", ", details)}{suffix}";
    }

    private static string? FormatDropboxItem(System.Text.Json.JsonElement item)
    {
        if (!item.TryGetProperty("name", out var name))
        {
            return null;
        }

        var size = item.TryGetProperty("size", out var sizeElement) &&
                   sizeElement.ValueKind == System.Text.Json.JsonValueKind.Number
            ? $", {sizeElement.GetUInt64()} bytes"
            : string.Empty;
        return $"{name.GetString()}{size}";
    }

    private static void ValidateDropboxPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("A Dropbox path must start with '/'.", nameof(path));
        }
    }
}
