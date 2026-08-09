using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Deterministically compiles search themes into query intents.
/// </summary>
internal static class QueryCompiler
{
    /// <summary>
    /// The date qualifier prefix for sliding window queries.
    /// </summary>
    private const string DateQualifierPrefix = "after:";

    /// <summary>
    /// Compiles themes into query intents for a given date window.
    /// </summary>
    /// <param name="themes">The search themes.</param>
    /// <param name="sourceLanguage">The BCP-47 source language qualifier.</param>
    /// <param name="daysBack">The number of days back for the date filter, or 0 for no filter.</param>
    /// <param name="preferredDomains">The preferred domains for domain-targeted queries.</param>
    /// <param name="budgetRemaining">The remaining query budget.</param>
    /// <returns>The ordered compiled queries with theme indices.</returns>
    public static IReadOnlyList<CompiledQuery> Compile(
        IReadOnlyList<Theme> themes,
        string sourceLanguage,
        int daysBack,
        IReadOnlyList<string> preferredDomains,
        int budgetRemaining)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        if (themes.Count == 0)
        {
            return [];
        }

        var queries = new List<CompiledQuery>();
        var dateQualifier = daysBack > 0
            ? $" {DateQualifierPrefix}{daysBack}d"
            : string.Empty;
        var langQualifier = $" lang:{sourceLanguage}";

        for (var themeIndex = 0; themeIndex < themes.Count && queries.Count < budgetRemaining; themeIndex++)
        {
            var theme = themes[themeIndex];

            // Current change query
            var currentChangeQuery = BuildQuery(
                theme,
                "current change",
                dateQualifier,
                langQualifier);
            queries.Add(new CompiledQuery(
                currentChangeQuery,
                themeIndex,
                QueryIntent.CurrentChange));

            if (queries.Count >= budgetRemaining)
            {
                break;
            }

            // Practical engineering query
            var practicalQuery = BuildQuery(
                theme,
                "practical engineering",
                dateQualifier,
                langQualifier);
            queries.Add(new CompiledQuery(
                practicalQuery,
                themeIndex,
                QueryIntent.PracticalEngineering));

            // Domain-targeted query (only if budget permits and preferred domains exist)
            if (preferredDomains.Count > 0 && queries.Count < budgetRemaining)
            {
                var domainQuery = BuildDomainQuery(
                    theme,
                    preferredDomains[0],
                    dateQualifier,
                    langQualifier);
                queries.Add(new CompiledQuery(
                    domainQuery,
                    themeIndex,
                    QueryIntent.DomainTargeted));
            }
        }

        return queries;
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

    private static string BuildDomainQuery(
        Theme theme,
        string domain,
        string dateQualifier,
        string langQualifier)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(" ", theme.Terms));
        builder.Append(" site:");
        builder.Append(domain);
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
    /// <summary>Query for current changes.</summary>
    CurrentChange,

    /// <summary>Query for practical engineering information.</summary>
    PracticalEngineering,

    /// <summary>Query targeting specific domains.</summary>
    DomainTargeted,
}
