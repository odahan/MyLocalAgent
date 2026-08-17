using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Deterministically compiles search themes into bounded primary and fallback queries.
/// </summary>
internal static class QueryCompiler
{
    /// <summary>
    /// The date qualifier prefix for sliding window queries.
    /// </summary>
    private const string DateQualifierPrefix = "after:";

    /// <summary>
    /// Compiles one primary query per theme for a given date window.
    /// </summary>
    /// <param name="themes">The search themes.</param>
    /// <param name="sourceLanguage">The BCP-47 source language qualifier.</param>
    /// <param name="earliestPublicationDate">The inclusive local publication date, or <see langword="null"/> for no filter.</param>
    /// <param name="budgetRemaining">The remaining query budget.</param>
    /// <returns>The ordered compiled queries with theme indices.</returns>
    public static IReadOnlyList<CompiledQuery> Compile(
        IReadOnlyList<Theme> themes,
        string sourceLanguage,
        DateOnly? earliestPublicationDate,
        int budgetRemaining)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        if (themes.Count == 0)
        {
            return [];
        }

        var queries = new List<CompiledQuery>();
        var dateQualifier = earliestPublicationDate.HasValue
            ? $" {DateQualifierPrefix}{earliestPublicationDate.Value:yyyy-MM-dd}"
            : string.Empty;
        var langQualifier = $" lang:{sourceLanguage}";

        for (var themeIndex = 0; themeIndex < themes.Count && queries.Count < budgetRemaining; themeIndex++)
        {
            var theme = themes[themeIndex];

            var currentChangeQuery = BuildQuery(
                theme,
                "current change",
                dateQualifier,
                langQualifier);
            queries.Add(new CompiledQuery(
                currentChangeQuery,
                themeIndex,
                QueryIntent.Primary));
        }

        return queries;
    }

    /// <summary>
    /// Compiles the single fallback query for one theme.
    /// </summary>
    public static CompiledQuery CompileFallback(
        Theme theme,
        int themeIndex,
        string sourceLanguage,
        DateOnly? earliestPublicationDate)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        var dateQualifier = earliestPublicationDate.HasValue
            ? $" {DateQualifierPrefix}{earliestPublicationDate.Value:yyyy-MM-dd}"
            : string.Empty;
        var langQualifier = $" lang:{sourceLanguage}";
        return new CompiledQuery(
            BuildQuery(theme, "practical engineering", dateQualifier, langQualifier),
            themeIndex,
            QueryIntent.Fallback);
    }

    private static string BuildQuery(
        Theme theme,
        string intentLabel,
        string dateQualifier,
        string langQualifier)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(" ", theme.Terms));
        builder.Append(' ');
        builder.Append(intentLabel);

        if (theme.IncludeTerms is { Count: > 0 })
        {
            builder.Append(' ');
            builder.Append(string.Join(" ", theme.IncludeTerms));
        }

        if (theme.ExcludeTerms is { Count: > 0 })
        {
            foreach (var exclude in theme.ExcludeTerms)
            {
                builder.Append(" -");
                builder.Append(exclude);
            }
        }

        builder.Append(langQualifier);
        builder.Append(dateQualifier);

        return builder.ToString();
    }
}

/// <summary>
/// Represents a compiled search query with its source theme and intent.
/// </summary>
internal sealed record CompiledQuery(
    string Query,
    int ThemeIndex,
    QueryIntent Intent);

/// <summary>
/// The type of search intent behind a compiled query.
/// </summary>
internal enum QueryIntent
{
    /// <summary>The mandatory current-change query for a theme.</summary>
    Primary,

    /// <summary>The optional practical-engineering query for a theme.</summary>
    Fallback,
}
