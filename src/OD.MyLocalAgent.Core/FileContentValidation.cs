namespace OD.MyLocalAgent.Core;

/// <summary>Provides shared authorization and bounded-input checks for file-content tools.</summary>
internal static class FileContentValidation
{
    /// <summary>Resolves and validates a bounded, regular input file.</summary>
    public static string ResolveFile(IPathSandbox sandbox, string requestedPath, ToolExecutionContext context, FileContentOptions options)
    {
        var path = sandbox.Resolve(requestedPath, FileSystemAccessMode.ReadOnly, context);
        var info = new FileInfo(path);
        if (!info.Exists || Directory.Exists(path)) throw new FileContentToolException("CorruptFile");
        if (info.Length > options.MaxInputFileBytes) throw new FileContentToolException("LimitExceeded");
        return path;
    }

    /// <summary>Returns a positive caller limit that cannot exceed its configured ceiling.</summary>
    public static int Limit(int? requested, int ceiling)
    {
        if (requested is <= 0) throw new ArgumentOutOfRangeException(nameof(requested));
        if (requested > ceiling) throw new ArgumentOutOfRangeException(nameof(requested));
        return requested ?? ceiling;
    }

    /// <summary>Normalizes line endings and replaces disallowed control characters with spaces.</summary>
    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return string.Create(normalized.Length, normalized, static (destination, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                destination[index] = char.IsControl(character) && character is not ('\n' or '\t') ? ' ' : character;
            }
        });
    }

    /// <summary>Returns a UTF-16 prefix without splitting a surrogate pair.</summary>
    public static string TruncateAtScalarBoundary(string value, int maximumCharacters)
    {
        if (value.Length <= maximumCharacters) return value;
        var length = maximumCharacters;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length];
    }
}
