using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Markdig;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements sandboxed local file-system tools.
/// </summary>
[ToolGroup(
    "filesystem",
    "File system",
    "Read or change files beneath authorized local paths.",
    400,
    Activation = ToolActivationKind.Contextual,
    ServiceContract = typeof(IFileSystemTool))]
public sealed class FileSystemTool : IFileSystemTool
{
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAdvancedExtensions()
            .Build();

    private readonly IPathSandbox _sandbox;
    private readonly IOptions<FileSystemOptions> _fileSystemOptions;
    private readonly IRecycleBinService _recycleBinService;
    private readonly ILogger<FileSystemTool> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ToolExecutionContext _executionContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemTool"/> class.
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public FileSystemTool(
        IPathSandbox sandbox,
        IOptions<FileSystemOptions> fileSystemOptions,
        IRecycleBinService recycleBinService,
        ILogger<FileSystemTool> logger,
        TimeProvider timeProvider,
        ToolExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        ArgumentNullException.ThrowIfNull(fileSystemOptions);
        ArgumentNullException.ThrowIfNull(recycleBinService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(executionContext);
        _sandbox = sandbox;
        _fileSystemOptions = fileSystemOptions;
        _recycleBinService = recycleBinService;
        _logger = logger;
        _timeProvider = timeProvider;
        _executionContext = executionContext;
    }

    /// <inheritdoc />
    [ToolLocalPath("path", LocalPathTargetKind.Directory, FileSystemAccessMode.ReadOnly)]
    [Tool("filesystem.list", "Lists immediate files and directories beneath an authorized local path.", 100)]
    public Task<string> ListAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root path or task-authorized absolute directory path.", Example = "inbox")] string path,
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
                                return (entry, attributes);
                            })
                            .Where(static t =>
                                !t.attributes.HasFlag(FileAttributes.Hidden) &&
                                !t.attributes.HasFlag(FileAttributes.System))
                            .Where(t => !_sandbox.IsApplicationOwnedPath(t.entry))
                            .Select(static t =>
                            {
                                var isDirectory = t.attributes.HasFlag(FileAttributes.Directory);
                                return new
                                {
                                    name = Path.GetFileName(t.entry),
                                    type = isDirectory ? "directory" : "file",
                                    size = isDirectory ? (long?)null : new FileInfo(t.entry).Length,
                                    createdUtc = new DateTimeOffset(
                                        File.GetCreationTimeUtc(t.entry),
                                        TimeSpan.Zero),
                                    lastModifiedUtc = new DateTimeOffset(
                                        File.GetLastWriteTimeUtc(t.entry),
                                        TimeSpan.Zero),
                                };
                            })
                            .ToArray();
                    },
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(new { path = Normalize(path), entries });
            });

    /// <inheritdoc />
    [ToolLocalPath("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("filesystem.read", "Reads a UTF-8 text file beneath an authorized local path.", 200)]
    public Task<string> ReadAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root path or task-authorized absolute file path.", Example = "inbox/source.txt")] string path,
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
    [ToolLocalPath("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]
    [Tool("filesystem.write", "Writes UTF-8 text to an authorized local file path.", 300)]
    public Task<string> WriteAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root path or task-authorized absolute file path.", Example = "reports/summary.txt")] string path,
        [ToolArgument(WorkflowArgumentKind.String, "Text content to write.", Example = "Report body.")] string content,
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
    [ToolLocalPath("from", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadWrite)]
    [ToolLocalPath("to", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadWrite)]
    [Tool("filesystem.move", "Moves a file or directory between authorized local paths.", 400)]
    public Task<string> MoveAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute source path.", Example = "inbox/source.txt")] string from,
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute destination path.", Example = "archive/destination.txt")] string to,
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
                            if (File.Exists(absoluteTo))
                            {
                                throw new IOException(
                                    $"Destination file '{to}' already exists. " +
                                    $"Choose a different destination name (e.g. add _2, _3, … before the extension) and retry.");
                            }

                            File.Move(absoluteFrom, absoluteTo);
                            return;
                        }

                        if (Directory.Exists(absoluteFrom))
                        {
                            if (Directory.Exists(absoluteTo))
                            {
                                throw new IOException(
                                    $"Destination directory '{to}' already exists. " +
                                    $"Choose a different destination name and retry.");
                            }

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
    [ToolLocalPath("path", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadWrite)]
    [Tool("filesystem.moveToBin", "Moves an authorized local file or directory to the Windows Recycle Bin when the task has Recycle Bin permission.", 500, RequiredCapability = TaskCapability.RecycleBin)]
    public Task<string> MoveToBinAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute file or directory path.", Example = "inbox/obsolete.txt")] string path,
        CancellationToken cancellationToken)
    {
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
    [ToolLocalPath("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]
    [ToolLocalPath("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite, Derivation = LocalPathDerivationKind.SameDirectoryLeafName, ValueArgumentName = "newName")]
    [Tool("filesystem.rename", "Renames an authorized local file without changing its directory.", 600)]
    public Task<string> RenameAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute source file path.", Example = "inbox/source.txt")] string path,
        [ToolArgument(WorkflowArgumentKind.String, "New file name only, without a directory path.", Example = "renamed.txt")] string newName,
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
    [ToolLocalPath("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]
    [ToolLocalPath("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite, Derivation = LocalPathDerivationKind.SameDirectoryExtension, ValueArgumentName = "extension")]
    [Tool("filesystem.setExtension", "Changes the extension of an authorized local file.", 700)]
    public Task<string> SetExtensionAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute source file path.", Example = "inbox/source.txt")] string path,
        [ToolArgument(WorkflowArgumentKind.String, "New extension with or without a leading period, for example '.md' or 'md'.", Example = ".md")] string extension,
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
    [ToolLocalPath("path", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadWrite)]
    [Tool("filesystem.delete", "Permanently deletes an authorized file or directory when the task has deletion permission.", 800, RequiredCapability = TaskCapability.PermanentFileDeletion)]
    public Task<string> DeleteAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute file or directory path.", Example = "inbox/obsolete.txt")] string path,
        CancellationToken cancellationToken)
    {
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
    [ToolLocalPath("path", LocalPathTargetKind.Directory, FileSystemAccessMode.ReadWrite)]
    [Tool("filesystem.createDirectory", "Creates a directory beneath an authorized local path.", 900)]
    public Task<string> CreateDirectoryAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute directory path.", Example = "reports/2026")] string path,
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
    [ToolLocalPath("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("filesystem.computeHash", "Computes a SHA-256 hash for an authorized file.", 950)]
    public Task<string> ComputeHashAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute file path.", Example = "reports/summary.md")] string path,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.computeHash",
            new { path },
            async () =>
            {
                var absolutePath = _sandbox.Resolve(
                    path,
                    FileSystemAccessMode.ReadOnly,
                    _executionContext);
                var fileHash = await ComputeFileHashAsync(
                    absolutePath,
                    path,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(
                    new
                    {
                        path = Normalize(path),
                        algorithm = "SHA-256",
                        hash = fileHash.Hash,
                        fileSize = fileHash.FileSize,
                    });
            });

    /// <inheritdoc />
    [ToolLocalPath("pathA", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [ToolLocalPath("pathB", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("filesystem.compareByHash", "Compares two authorized files using SHA-256 hashes.", 960)]
    public Task<string> CompareByHashAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute first file path.", Example = "reports/summary-a.md")] string pathA,
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute second file path.", Example = "reports/summary-b.md")] string pathB,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "filesystem.compareByHash",
            new { pathA, pathB },
            async () =>
            {
                var absolutePathA = _sandbox.Resolve(
                    pathA,
                    FileSystemAccessMode.ReadOnly,
                    _executionContext);
                var absolutePathB = _sandbox.Resolve(
                    pathB,
                    FileSystemAccessMode.ReadOnly,
                    _executionContext);
                var fileHashA = await ComputeFileHashAsync(
                    absolutePathA,
                    pathA,
                    cancellationToken).ConfigureAwait(false);
                var fileHashB = await ComputeFileHashAsync(
                    absolutePathB,
                    pathB,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(
                    new
                    {
                        pathA = Normalize(pathA),
                        pathB = Normalize(pathB),
                        algorithm = "SHA-256",
                        hashA = fileHashA.Hash,
                        hashB = fileHashB.Hash,
                        isEqual = string.Equals(fileHashA.Hash, fileHashB.Hash, StringComparison.Ordinal),
                    });
            });

    /// <inheritdoc />
    [ToolLocalPath("sourcePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [ToolLocalPath("sourcePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite, Derivation = LocalPathDerivationKind.OptionalDestinationOrSameDirectoryExtension, ValueArgumentName = "destinationPath", DefaultValue = ".html")]
    [Tool("filesystem.markdownToHtml", "Converts an authorized Markdown file to a standalone UTF-8 HTML file using a dark or light theme.", 1000)]
    public Task<string> MarkdownToHtmlAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute Markdown source file path.", Example = "reports/summary.md")] string sourcePath,
        [ToolArgument(WorkflowArgumentKind.String, "Optional HTML destination path; omit to replace the source extension with '.html'.", Example = "reports/summary.html")] string? destinationPath = null,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Standalone HTML theme: Dark or Light. Dark is the default.", Example = "Dark")] MarkdownHtmlTheme theme = MarkdownHtmlTheme.Dark,
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
    [ToolLocalPath("sourcePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("filesystem.htmlToText", "Extracts normalized plain text or principal Markdown structure from an authorized HTML file.", 1100)]
    public Task<string> HtmlToTextAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Relative global-root or task-authorized absolute HTML source file path.", Example = "inbox/page.html")] string sourcePath,
        [ToolArgument(WorkflowArgumentKind.Enumeration, "Output format: Text or Markdown. Text is the default.", Example = "Text")] HtmlTextFormat format = HtmlTextFormat.Text,
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

    /// <inheritdoc />
    [Tool("filesystem.shellExecute", "Opens a file, URI, or application through the Windows shell when the task has Shell Execute permission.", 1200, RequiredCapability = TaskCapability.ShellExecute)]
    public Task<string> ShellExecuteAsync(
        [ToolArgument(WorkflowArgumentKind.String, "File path, URI, or application to open through the Windows shell.", Example = "https://example.com")] string target,
        [ToolArgument(WorkflowArgumentKind.String, "Optional arguments passed to an application target.", Example = "--help")] string? arguments = null,
        [ToolArgument(WorkflowArgumentKind.Boolean, "Whether to wait for the started process to exit. False is the default.", Example = "false")] bool waitForExit = false,
        [ToolArgument(WorkflowArgumentKind.Boolean, "Whether to capture standard output and error. Windows shell execution cannot capture output, so this must be false.", Example = "false")] bool captureOutput = false,
        CancellationToken cancellationToken = default)
    {
        if (captureOutput)
        {
            throw new ArgumentException(
                "Windows shell execution cannot capture output. Set captureOutput to false.",
                nameof(captureOutput));
        }

        return ExecuteProcessAsync(
            "filesystem.shellExecute",
            new ProcessStartInfo
            {
                FileName = target,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
            },
            target,
            waitForExit,
            captureOutput,
            cancellationToken);
    }

    /// <inheritdoc />
    [Tool("filesystem.powerShell", "Executes a PowerShell script locally when the task has PowerShell permission.", 1300, RequiredCapability = TaskCapability.PowerShellExecution)]
    public Task<string> PowerShellAsync(
        [ToolArgument(WorkflowArgumentKind.String, "PowerShell script to execute.", Example = "Get-Date", IsLongText = true)] string script,
        [ToolArgument(WorkflowArgumentKind.Boolean, "Whether to wait for the PowerShell process to exit. True is the default.", Example = "true")] bool waitForExit = true,
        [ToolArgument(WorkflowArgumentKind.Boolean, "Whether to return standard output and error. Requires waitForExit to be true.", Example = "true")] bool captureOutput = true,
        CancellationToken cancellationToken = default)
    {
        if (captureOutput && !waitForExit)
        {
            throw new ArgumentException(
                "Output can only be captured when waitForExit is true.",
                nameof(captureOutput));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        return ExecuteProcessAsync(
            "filesystem.powerShell",
            startInfo,
            "powershell.exe",
            waitForExit,
            captureOutput,
            cancellationToken);
    }

    private Task<string> ExecuteProcessAsync(
        string operation,
        ProcessStartInfo startInfo,
        string target,
        bool waitForExit,
        bool captureOutput,
        CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            operation,
            new { target, waitForExit, captureOutput },
            async () =>
            {
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"Unable to start '{target}'.");
                if (!waitForExit)
                {
                    return ToolJson.Serialize(
                        new { target, started = true, waitForExit = false, captureOutput = false });
                }

                Task<string>? outputTask = captureOutput
                    ? process.StandardOutput.ReadToEndAsync(cancellationToken)
                    : null;
                Task<string>? errorTask = captureOutput
                    ? process.StandardError.ReadToEndAsync(cancellationToken)
                    : null;
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                return ToolJson.Serialize(
                    new
                    {
                        target,
                        started = true,
                        waitForExit = true,
                        exitCode = process.ExitCode,
                        captureOutput,
                        output = outputTask is null ? null : await outputTask.ConfigureAwait(false),
                        error = errorTask is null ? null : await errorTask.ConfigureAwait(false),
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

    private static async Task<(string Hash, long FileSize)> ComputeFileHashAsync(
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

        await using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return (Convert.ToHexString(hash).ToLowerInvariant(), file.Length);
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
