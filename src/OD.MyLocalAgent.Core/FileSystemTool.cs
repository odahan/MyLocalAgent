using System.ComponentModel;
using System.Text;
using Markdig;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements sandboxed local file-system tools.
/// </summary>
public sealed class FileSystemTool : IFileSystemTool
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();

    private readonly IPathSandbox _sandbox;
    private readonly IOptions<SafetyOptions> _safetyOptions;
    private readonly IOptions<FileSystemOptions> _fileSystemOptions;
    private readonly IRecycleBinService _recycleBinService;
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
        IRecycleBinService recycleBinService,
        ILogger<FileSystemTool> logger,
        TimeProvider timeProvider,
        ToolExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(safetyOptions);
        ArgumentNullException.ThrowIfNull(fileSystemOptions);
        ArgumentNullException.ThrowIfNull(recycleBinService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(executionContext);
        _sandbox = sandbox;
        _safetyOptions = safetyOptions;
        _fileSystemOptions = fileSystemOptions;
        _recycleBinService = recycleBinService;
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
    [Description("Moves an authorized local file or directory to the Windows Recycle Bin when Safety:AllowMoveToBin is enabled.")]
    public Task<string> MoveToBinAsync(
        [Description("Relative global-root or task-authorized absolute file or directory path.")] string path,
        CancellationToken cancellationToken)
    {
        if (!_safetyOptions.Value.AllowMoveToBin)
        {
            throw new SafetyException(
                "filesystem.moveToBin",
                "Safety:AllowMoveToBin");
        }

        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.moveToBin",
            new { path },
            async () =>
            {
                var absolutePath = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                if (!File.Exists(absolutePath) && !Directory.Exists(absolutePath))
                {
                    throw new FileNotFoundException(
                        $"Path '{path}' does not exist.",
                        path);
                }

                await _recycleBinService
                    .MoveToBinAsync(absolutePath, cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(
                    new { path = Normalize(path), movedToBin = true });
            });
    }

    /// <inheritdoc />
    [Description("Renames an authorized local file without changing its directory.")]
    public Task<string> RenameAsync(
        [Description("Relative global-root or task-authorized absolute source file path.")] string path,
        [Description("New file name only, without a directory path.")] string newName,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.rename",
            new { path, newName },
            async () =>
            {
                ValidateFileName(newName, nameof(newName));
                var absoluteSource = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                var destinationPath = Path.Combine(
                    Path.GetDirectoryName(path) ?? string.Empty,
                    newName);
                var absoluteDestination = _sandbox.Resolve(
                    destinationPath,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                var changed = !string.Equals(
                    absoluteSource,
                    absoluteDestination,
                    StringComparison.OrdinalIgnoreCase);
                await Task.Run(
                    () =>
                    {
                        if (!File.Exists(absoluteSource))
                        {
                            throw new FileNotFoundException(
                                $"File '{path}' does not exist.",
                                path);
                        }

                        if (changed)
                        {
                            File.Move(absoluteSource, absoluteDestination);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                return ToolJson.Serialize(
                    new
                    {
                        from = Normalize(path),
                        to = Normalize(destinationPath),
                        renamed = changed,
                    });
            });

    /// <inheritdoc />
    [Description("Changes the extension of an authorized local file.")]
    public Task<string> SetExtensionAsync(
        [Description("Relative global-root or task-authorized absolute source file path.")] string path,
        [Description("New extension with or without a leading period, for example '.md' or 'md'.")] string extension,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.setExtension",
            new { path, extension },
            async () =>
            {
                var normalizedExtension = NormalizeExtension(extension);
                var destinationPath = Path.ChangeExtension(path, normalizedExtension);
                var absoluteSource = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                var absoluteDestination = _sandbox.Resolve(
                    destinationPath,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                var changed = !string.Equals(
                    absoluteSource,
                    absoluteDestination,
                    StringComparison.OrdinalIgnoreCase);
                await Task.Run(
                    () =>
                    {
                        if (!File.Exists(absoluteSource))
                        {
                            throw new FileNotFoundException(
                                $"File '{path}' does not exist.",
                                path);
                        }

                        if (changed)
                        {
                            File.Move(absoluteSource, absoluteDestination);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                return ToolJson.Serialize(
                    new
                    {
                        from = Normalize(path),
                        to = Normalize(destinationPath),
                        extension = normalizedExtension,
                        changed,
                    });
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

    /// <inheritdoc />
    [Description("Converts an authorized Markdown file to a standalone UTF-8 HTML file using a dark or light theme.")]
    public Task<string> MarkdownToHtmlAsync(
        [Description("Relative global-root or task-authorized absolute Markdown source file path.")] string sourcePath,
        [Description("Optional HTML destination path; omit to replace the source extension with '.html'.")] string? destinationPath = null,
        [Description("Standalone HTML theme: Dark or Light. Dark is the default.")] MarkdownHtmlTheme theme = MarkdownHtmlTheme.Dark,
        CancellationToken cancellationToken = default) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.markdownToHtml",
            new { sourcePath, destinationPath, theme },
            async () =>
            {
                var absoluteSource = _sandbox.Resolve(
                    sourcePath,
                    FileSystemAccessMode.ReadOnly,
                    _executionContext);
                var effectiveDestination = string.IsNullOrWhiteSpace(destinationPath)
                    ? Path.ChangeExtension(sourcePath, ".html")
                    : destinationPath;
                var absoluteDestination = _sandbox.Resolve(
                    effectiveDestination,
                    FileSystemAccessMode.ReadWrite,
                    _executionContext);
                if (string.Equals(
                        absoluteSource,
                        absoluteDestination,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "The Markdown source and HTML destination must be different paths.",
                        nameof(destinationPath));
                }

                var markdown = await ReadAuthorizedTextAsync(
                    absoluteSource,
                    sourcePath,
                    cancellationToken).ConfigureAwait(false);
                var body = Markdown.ToHtml(markdown, MarkdownPipeline);
                var html = BuildStandaloneHtml(body, theme);
                var parent = Path.GetDirectoryName(absoluteDestination);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                await File.WriteAllTextAsync(
                    absoluteDestination,
                    html,
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(
                    new
                    {
                        sourcePath = Normalize(sourcePath),
                        destinationPath = Normalize(effectiveDestination),
                        theme = theme.ToString(),
                        bytes = Encoding.UTF8.GetByteCount(html),
                    });
            });

    /// <inheritdoc />
    [Description("Extracts normalized plain text or principal Markdown structure from an authorized HTML file.")]
    public Task<string> HtmlToTextAsync(
        [Description("Relative global-root or task-authorized absolute HTML source file path.")] string sourcePath,
        [Description("Output format: Text or Markdown. Text is the default.")] HtmlTextFormat format = HtmlTextFormat.Text,
        CancellationToken cancellationToken = default) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.htmlToText",
            new { sourcePath, format },
            async () =>
            {
                var absoluteSource = _sandbox.Resolve(
                    sourcePath,
                    FileSystemAccessMode.ReadOnly,
                    _executionContext);
                var html = await ReadAuthorizedTextAsync(
                    absoluteSource,
                    sourcePath,
                    cancellationToken).ConfigureAwait(false);
                var content = HtmlDocumentConverter.Convert(html, format);
                return ToolJson.Serialize(
                    new
                    {
                        sourcePath = Normalize(sourcePath),
                        format = format.ToString(),
                        content,
                    });
            });

    private async Task<string> ReadAuthorizedTextAsync(
        string absolutePath,
        string displayPath,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(absolutePath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                $"File '{displayPath}' does not exist.",
                displayPath);
        }

        if (file.Length > _fileSystemOptions.Value.MaxReadBytes)
        {
            throw new IOException(
                $"File '{displayPath}' exceeds the {_fileSystemOptions.Value.MaxReadBytes}-byte read limit.");
        }

        return await File.ReadAllTextAsync(
            absolutePath,
            Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);
    }

    private static string BuildStandaloneHtml(
        string body,
        MarkdownHtmlTheme theme)
    {
        var isDark = theme == MarkdownHtmlTheme.Dark;
        var colorScheme = isDark ? "dark" : "light";
        var background = isDark ? "#1e1e24" : "#fafafa";
        var foreground = isDark ? "#f1f1f4" : "#202124";
        var accent = isDark ? "#8ab4f8" : "#365f9d";
        var codeBackground = isDark ? "#292932" : "#eeeef2";
        var border = isDark ? "#555" : "#b8b8c0";
        var quote = isDark ? "#c8c8cf" : "#53535c";
        return
            $$"""
            <!doctype html>
            <html><head><meta charset="utf-8">
            <meta name="color-scheme" content="{{colorScheme}}">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; connect-src 'none'; frame-src 'none'; child-src 'none'; img-src 'none'; media-src 'none'; font-src 'none'; object-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <style>
            :root{color-scheme:{{colorScheme}}}body{max-width:960px;margin:0 auto;padding:32px;background:{{background}};color:{{foreground}};font:16px/1.6 Segoe UI,Arial,sans-serif}
            h1,h2,h3,h4,h5,h6{color:{{accent}}}code,pre{font-family:Consolas,monospace;background:{{codeBackground}}}pre{padding:12px;overflow:auto}
            a{color:{{accent}}}table{border-collapse:collapse}th,td{border:1px solid {{border}};padding:6px 9px}blockquote{border-left:3px solid {{border}};margin-left:0;padding-left:12px;color:{{quote}}}
            </style></head><body>
            """ +
            body +
            "</body></html>";
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/');

    private static void ValidateFileName(string fileName, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName, parameterName);
        if (Path.IsPathRooted(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "The new name must be a valid file name without a directory path.",
                parameterName);
        }
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var trimmed = extension.Trim();
        var withoutPeriod = trimmed.TrimStart('.');
        if (string.IsNullOrWhiteSpace(withoutPeriod) ||
            withoutPeriod.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            withoutPeriod.Contains(Path.DirectorySeparatorChar) ||
            withoutPeriod.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "The extension must contain only a file extension, with or without a leading period.",
                nameof(extension));
        }

        return "." + withoutPeriod;
    }
}
