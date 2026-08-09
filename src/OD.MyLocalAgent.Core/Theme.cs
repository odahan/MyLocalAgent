namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents a search theme with terms and optional include/exclude filters.
/// </summary>
/// <param name="Terms">The required search terms (at least one).</param>
/// <param name="IncludeTerms">Optional additional terms that must appear in results.</param>
/// <param name="ExcludeTerms">Optional terms that must not appear in results.</param>
public sealed record Theme(
    IReadOnlyList<string> Terms,
    IReadOnlyList<string>? IncludeTerms = null,
    IReadOnlyList<string>? ExcludeTerms = null);
