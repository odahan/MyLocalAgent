namespace OD.MyLocalAgent.Core;

/// <summary>
/// Translates a Guardrails-approved discovery payload without tools.
/// </summary>
public interface IResearchTranslationService
{
    /// <summary>
    /// Translates candidate titles and summaries while preserving candidate identifiers.
    /// </summary>
    /// <param name="protectedPayload">The nonce-wrapped, Guardrails-approved source payload.</param>
    /// <param name="expectedIds">The candidate identifiers that must be preserved exactly.</param>
    /// <param name="targetLanguage">The BCP-47 target language.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The validated translated candidates in source order.</returns>
    Task<IReadOnlyList<ResearchTranslationItem>> TranslateAsync(
        string protectedPayload,
        IReadOnlyList<string> expectedIds,
        string targetLanguage,
        CancellationToken cancellationToken);
}

/// <summary>
/// Carries one validated translated discovery candidate.
/// </summary>
/// <param name="Id">The preserved candidate identifier.</param>
/// <param name="Title">The translated title.</param>
/// <param name="ShortSummary">The translated compact summary.</param>
public sealed record ResearchTranslationItem(
    string Id,
    string Title,
    string ShortSummary);
