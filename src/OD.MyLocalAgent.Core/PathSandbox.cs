using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Enforces rooted-prefix path containment for local tool operations.
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
    public string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path))
        {
            throw new PathSandboxException(path);
        }

        string absolutePath;
        try
        {
            absolutePath = Path.GetFullPath(path, _rootPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PathSandboxException(path);
        }

        if (!string.Equals(absolutePath, _rootPath, _comparison) &&
            !absolutePath.StartsWith(_rootPrefix, _comparison))
        {
            throw new PathSandboxException(path);
        }

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
}
