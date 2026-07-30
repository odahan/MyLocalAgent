using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Enforces global-root and task-specific containment for local tool operations.
/// </summary>
public sealed class PathSandbox : IPathSandbox
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;
    private readonly StringComparison _comparison;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathSandbox"/> class.
    /// </summary>
    /// <param name="options">The configured sandbox root.</param>
    public PathSandbox(IOptions<FileSystemOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Value.RootPath) ||
            !Path.IsPathRooted(options.Value.RootPath))
        {
            throw new OptionsValidationException(
                FileSystemOptions.SectionName,
                typeof(FileSystemOptions),
                ["FileSystem:RootPath must be an absolute path."]);
        }

        _rootPath = Path.GetFullPath(options.Value.RootPath);
        _rootPrefix = Path.TrimEndingDirectorySeparator(_rootPath) +
            Path.DirectorySeparatorChar;
        _comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    /// <inheritdoc />
    public string Resolve(
        string path,
        FileSystemAccessMode requiredAccess,
        ToolExecutionContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);

        string absolutePath;
        string authorizedRoot;
        try
        {
            if (Path.IsPathRooted(path))
            {
                absolutePath = Path.GetFullPath(path);
                var matchingRule = context.FileSystemAccess
                    .Select(
                        static rule => new
                        {
                            Rule = rule,
                            NormalizedPath =
                                ScheduledTaskValidator.NormalizeAccessPath(rule.Path),
                        })
                    .Where(item => IsWithin(absolutePath, item.NormalizedPath))
                    .OrderByDescending(static item => item.NormalizedPath.Length)
                    .FirstOrDefault()
                    ?? throw new PathSandboxException(
                        path,
                        "no task authorization covers this absolute path.");
                if (requiredAccess == FileSystemAccessMode.ReadWrite &&
                    matchingRule.Rule.Access != FileSystemAccessMode.ReadWrite)
                {
                    throw new PathSandboxException(
                        path,
                        "the most specific task authorization is read-only.");
                }

                authorizedRoot = matchingRule.NormalizedPath;
            }
            else
            {
                absolutePath = Path.GetFullPath(path, _rootPath);
                if (!IsWithin(absolutePath, _rootPath))
                {
                    throw new PathSandboxException(path);
                }

                authorizedRoot = _rootPath;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PathSandboxException(path);
        }

        RejectReparsePointEscape(
            path,
            Path.GetPathRoot(authorizedRoot) ?? authorizedRoot,
            absolutePath);
        return absolutePath;
    }

    /// <inheritdoc />
    public string ToRelative(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        var fullPath = Path.GetFullPath(absolutePath);
        if (!string.Equals(fullPath, _rootPath, _comparison) &&
            !fullPath.StartsWith(_rootPrefix, _comparison))
        {
            throw new PathSandboxException(absolutePath);
        }

        var relative = Path.GetRelativePath(_rootPath, fullPath);
        return relative == "." ? string.Empty : relative.Replace('\\', '/');
    }

    private static void RejectReparsePointEscape(
        string requestedPath,
        string authorizedRoot,
        string absolutePath)
    {
        var current = authorizedRoot;
        while (true)
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new PathSandboxException(
                        requestedPath,
                        $"reparse point '{current}' could escape the authorized location.");
                }
            }

            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(current),
                    Path.TrimEndingDirectorySeparator(absolutePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var relative = Path.GetRelativePath(current, absolutePath);
            var separatorIndex = relative.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
            var nextComponent = separatorIndex < 0
                ? relative
                : relative[..separatorIndex];
            if (string.IsNullOrEmpty(nextComponent) ||
                string.Equals(nextComponent, ".", StringComparison.Ordinal))
            {
                return;
            }

            current = Path.Combine(current, nextComponent);
        }
    }

    private static bool IsWithin(string absolutePath, string rootPath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(rootPath);
        var normalizedPath = Path.TrimEndingDirectorySeparator(absolutePath);
        if (string.Equals(
                normalizedPath,
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(
            rootPrefix,
            StringComparison.OrdinalIgnoreCase);
    }
}
