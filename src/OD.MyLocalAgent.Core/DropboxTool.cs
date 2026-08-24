using System.ComponentModel;
using Microsoft.Extensions.Logging;
using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes compact-JSON Dropbox connector operations.
/// </summary>
[ToolGroup(
    "dropbox",
    "Dropbox",
    "List, upload, or download Dropbox content.",
    300,
    Activation = ToolActivationKind.Contextual,
    ServiceContract = typeof(IDropboxTool))]
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
    [ToolLocalPath("localPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("dropbox.upload", "Uploads an authorized local file to Dropbox.", 100)]
    public Task<string> UploadAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute local source path.", Example = "reports/summary.txt")] string localPath,
        [ToolArgument(WorkflowArgumentKind.String, "Dropbox destination path.", Example = "/Reports/summary.txt")] string dropboxPath,
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
    [ToolLocalPath("localPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite, PickerMode = LocalPathPickerMode.SaveFile)]
    [Tool("dropbox.download", "Downloads a Dropbox file into an authorized local path.", 200)]
    public Task<string> DownloadAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Dropbox source path.", Example = "/Reports/summary.txt")] string dropboxPath,
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute local destination path.", Example = "reports/summary.txt")] string localPath,
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
    [Tool("dropbox.list", "Lists the immediate children of a Dropbox folder.", 300)]
    public Task<string> ListAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Dropbox folder path.", Example = "/Reports")] string folderPath,
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
