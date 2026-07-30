using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements sandboxed local file-system tools.
/// </summary>
public sealed class FileSystemTool : IFileSystemTool
{
    private readonly IPathSandbox _sandbox;
    private readonly IOptions<SafetyOptions> _safetyOptions;
    private readonly IOptions<FileSystemOptions> _fileSystemOptions;
    private readonly ILogger<FileSystemTool> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ToolExecutionContext _executionContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTool"/> class.
    /// </summary>
    public FileSystemTool(
        IPathSandbox sandbox,
        IOptions<SafetyOptions> safetyOptions,
        IOptions<FileSystemOptions> fileSystemOptions,
        ILogger<FileSystemTool> logger,
        TimeProvider timeProvider,
        ToolExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(safetyOptions);
        ArgumentNullException.ThrowIfNull(fileSystemOptions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(executionContext);
        _sandbox = sandbox;
        _safetyOptions = safetyOptions;
        _fileSystemOptions = fileSystemOptions;
        _logger = logger;
        _timeProvider = timeProvider;
        _executionContext = executionContext;
    }

    /// <inheritdoc />
    [Description("Lists immediate files and directories beneath an authorized local path.")]
    public Task<string> ListAsync(
        [Description("Relative global-root path or task-authorized absolute directory path.")] string path,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.list",
            new { path },
            async () =>
            {
                var absolutePath = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadOnly,
                    _executionContext);
                var entries = await Task.Run(
                    () =>
                    {
                        if (!Directory.Exists(absolutePath))
                        {
                            throw new DirectoryNotFoundException(
                                $"Directory '{path}' does not exist.");
                        }

                        return Directory.EnumerateFileSystemEntries(absolutePath)
                            .Order(StringComparer.OrdinalIgnoreCase)
                            .Select(entry =>
                            {
                                var attributes = File.GetAttributes(entry);
                                var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                                return new
                                {
                                    name = Path.GetFileName(entry),
                                    type = isDirectory ? "directory" : "file",
                                    size = isDirectory ? (long?)null : new FileInfo(entry).Length,
                                };
                            })
                            .ToArray();
                    },
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(new { path = Normalize(path), entries });
            });

    /// <inheritdoc />
    [Description("Reads a UTF-8 text file beneath an authorized local path.")]
    public Task<string> ReadAsync(
        [Description("Relative global-root path or task-authorized absolute file path.")] string path,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.read",
            new { path },
            async () =>
            {
                var absolutePath = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadOnly,
                    _executionContext);
                var file = new FileInfo(absolutePath);
                if (!file.Exists)
                {
                    throw new FileNotFoundException($"File '{path}' does not exist.", path);
                }

                if (file.Length > _fileSystemOptions.Value.MaxReadBytes)
                {
                    throw new IOException(
                        $"File '{path}' exceeds the {_fileSystemOptions.Value.MaxReadBytes}-byte read limit.");
                }

                var content = await File.ReadAllTextAsync(
                    absolutePath,
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(new { path = Normalize(path), content });
            });

    /// <inheritdoc />
    [Description("Writes UTF-8 text to an authorized local file path.")]
    public Task<string> WriteAsync(
        [Description("Relative global-root path or task-authorized absolute file path.")] string path,
        [Description("Text content to write.")] string content,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.write",
            new { path, contentLength = content?.Length ?? 0 },
            async () =>
            {
                ArgumentNullException.ThrowIfNull(content);
                var absolutePath = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                var parent = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                await File.WriteAllTextAsync(
                    absolutePath,
                    content,
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(
                    new
                    {
                        path = Normalize(path),
                        bytes = Encoding.UTF8.GetByteCount(content),
                    });
            });

    /// <inheritdoc />
    [Description("Moves a file or directory between authorized local paths.")]
    public Task<string> MoveAsync(
        [Description("Relative global-root or task-authorized absolute source path.")] string from,
        [Description("Relative global-root or task-authorized absolute destination path.")] string to,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.move",
            new { from, to },
            async () =>
            {
                var absoluteFrom = _sandbox.Resolve(
                    from,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                var absoluteTo = _sandbox.Resolve(
                    to,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                await Task.Run(
                    () =>
                    {
                        if (File.Exists(absoluteFrom))
                        {
                            File.Move(absoluteFrom, absoluteTo);
                            return;
                        }

                        if (Directory.Exists(absoluteFrom))
                        {
                            Directory.Move(absoluteFrom, absoluteTo);
                            return;
                        }

                        throw new FileNotFoundException(
                            $"Source path '{from}' does not exist.",
                            from);
                    },
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(
                    new { from = Normalize(from), to = Normalize(to), moved = true });
            });

    /// <inheritdoc />
    [Description("Deletes a sandboxed file or directory when Safety:AllowFileDelete is enabled.")]
    public Task<string> DeleteAsync(
        [Description("Relative global-root or task-authorized absolute file or directory path.")] string path,
        CancellationToken cancellationToken)
    {
        if (!_safetyOptions.Value.AllowFileDelete)
        {
            throw new SafetyException(
                "filesystem.delete",
                "Safety:AllowFileDelete");
        }

        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.delete",
            new { path },
            async () =>
            {
                var absolutePath = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                await Task.Run(
                    () =>
                    {
                        if (File.Exists(absolutePath))
                        {
                            File.Delete(absolutePath);
                            return;
                        }

                        if (Directory.Exists(absolutePath))
                        {
                            Directory.Delete(absolutePath, recursive: true);
                            return;
                        }

                        throw new FileNotFoundException(
                            $"Path '{path}' does not exist.",
                            path);
                    },
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(new { path = Normalize(path), deleted = true });
            });
    }

    /// <inheritdoc />
    [Description("Creates a directory beneath an authorized local path.")]
    public Task<string> CreateDirectoryAsync(
        [Description("Relative global-root or task-authorized absolute directory path.")] string path,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.createDirectory",
            new { path },
            async () =>
            {
                var absolutePath = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                await Task.Run(
                    () => Directory.CreateDirectory(absolutePath),
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(new { path = Normalize(path), created = true });
            });

    private static string Normalize(string path) =>
        path.Replace('\\', '/');
}
