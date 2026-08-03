using System.Globalization;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Parses and formats the shared workflow and agent-prompt date/time placeholders.
/// </summary>
public static class DateTimePlaceholderFormatter
{
    private const int MaximumFormatLength = 100;
    private const string TodayName = "Today";
    private const string NowName = "Now";

    /// <summary>
    /// Resolves a trimmed placeholder body against one explicit local-time snapshot.
    /// </summary>
    /// <param name="placeholder">The trimmed placeholder body.</param>
    /// <param name="localNow">The local-time snapshot used for formatting.</param>
    /// <returns>A typed parsing and formatting result.</returns>
    public static DateTimePlaceholderResult Resolve(
        string placeholder,
        DateTimeOffset localNow)
    {
        ArgumentNullException.ThrowIfNull(placeholder);

        var name = MatchName(placeholder);
        if (name is null)
        {
            return new DateTimePlaceholderResult(
                DateTimePlaceholderStatus.NotRecognized,
                null);
        }

        var suffix = placeholder[name.Length..];
        string format;
        if (suffix.Length == 0)
        {
            format = name == TodayName
                ? "yyyy-MM-dd"
                : "yyyy-MM-ddTHH:mm:sszzz";
        }
        else
        {
            if (suffix[0] != '[' ||
                suffix.Length < 2 ||
                suffix[^1] != ']' ||
                suffix.AsSpan(1, suffix.Length - 2).IndexOfAny('[', ']') >= 0)
            {
                return new DateTimePlaceholderResult(
                    DateTimePlaceholderStatus.InvalidFormat,
                    null);
            }

            format = suffix[1..^1];
            if (string.IsNullOrWhiteSpace(format) ||
                format.Length > MaximumFormatLength)
            {
                return new DateTimePlaceholderResult(
                    DateTimePlaceholderStatus.InvalidFormat,
                    null);
            }

            format = NormalizeFormat(format);
        }

        try
        {
            return new DateTimePlaceholderResult(
                DateTimePlaceholderStatus.Resolved,
                localNow.ToString(format, CultureInfo.InvariantCulture));
        }
        catch (FormatException)
        {
            return new DateTimePlaceholderResult(
                DateTimePlaceholderStatus.InvalidFormat,
                null);
        }
    }

    /// <summary>
    /// Normalizes the documented filename-friendly custom-format aliases.
    /// </summary>
    /// <param name="format">The user-entered .NET custom date/time format.</param>
    /// <returns>The normalized .NET custom date/time format.</returns>
    public static string NormalizeFormat(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format
            .Replace("YYYY", "yyyy", StringComparison.Ordinal)
            .Replace("YY", "yy", StringComparison.Ordinal)
            .Replace("DD", "dd", StringComparison.Ordinal)
            .Replace("hh", "HH", StringComparison.Ordinal);
    }

    private static string? MatchName(string placeholder)
    {
        if (placeholder.StartsWith(TodayName, StringComparison.OrdinalIgnoreCase) &&
            (placeholder.Length == TodayName.Length ||
             placeholder[TodayName.Length] == '['))
        {
            return TodayName;
        }

        if (placeholder.StartsWith(NowName, StringComparison.OrdinalIgnoreCase) &&
            (placeholder.Length == NowName.Length ||
             placeholder[NowName.Length] == '['))
        {
            return NowName;
        }

        return null;
    }
}
