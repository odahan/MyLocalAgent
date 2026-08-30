namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides web discovery operations to agents and workflows.
/// </summary>
public interface IResearchDiscoverTool
{
    /// <summary>
    /// Discovers web documents matching the given themes.
    /// </summary>
    /// <param name="themes">The JSON-serialized theme array.</param>
    /// <param name="freshnessWindow">The freshness window mode.</param>
    /// <param name="sourceLanguage">The BCP-47 source language tag for search.</param>
    /// <param name="language">The BCP-47 target language for translation, or empty.</param>
    /// <param name="preferredDomains">The JSON-serialized preferred domains array.</param>
    /// <param name="minimumDocuments">The minimum unique dated documents target.</param>
    /// <param name="maxResultsPerQuery">The advanced compatibility limit for search hits per request.</param>
    /// <param name="maxRetrievalsPerRun">The advanced compatibility safeguard for page fetch attempts.</param>
    /// <param name="previousProposalsDatabasePath">The optional authorized proposal-memory database path.</param>
    /// <param name="previousProposalsLookbackMonths">The proposal-memory lookback period in whole months.</param>
    /// <param name="usePreviousProposalUsedTag">Whether old proposals marked as used remain applicable.</param>
    /// <param name="previousProposalsSemanticSimilarityThreshold">The exact cosine-similarity exclusion threshold.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Structured discovery results as compact JSON.</returns>
    Task<string> DiscoverAsync(
        string themes,
        string freshnessWindow,
        string sourceLanguage,
        string language,
        string preferredDomains,
        int minimumDocuments,
        int maxResultsPerQuery,
        int maxRetrievalsPerRun,
        string previousProposalsDatabasePath = "",
        int previousProposalsLookbackMonths = 6,
        bool usePreviousProposalUsedTag = false,
        double previousProposalsSemanticSimilarityThreshold = 0.92,
        CancellationToken cancellationToken = default);
}
