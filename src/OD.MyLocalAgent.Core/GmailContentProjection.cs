namespace OD.MyLocalAgent.Core;

/// <summary>
/// Carries one deterministic bounded Gmail projection and content-free metadata.
/// </summary>
/// <param name="Content">The complete compact canonical source.</param>
/// <param name="From">The exact projected sender field.</param>
/// <param name="To">The exact projected recipient field.</param>
/// <param name="Subject">The exact projected subject field.</param>
/// <param name="Received">The invariant receipt timestamp.</param>
/// <param name="OriginalCanonicalCharacterCount">The normalized uncapped canonical size.</param>
/// <param name="ProjectedCharacterCount">The compact canonical size.</param>
/// <param name="OmittedCharacterCount">The aggregate omitted source-character count.</param>
/// <param name="ProjectionWasTruncated">Whether any source text was omitted.</param>
/// <param name="QuotedHistoryWasRemoved">Whether a recognized quoted suffix was removed.</param>
public sealed record GmailContentProjection(
    string Content,
    string From,
    string To,
    string Subject,
    string Received,
    int OriginalCanonicalCharacterCount,
    int ProjectedCharacterCount,
    int OmittedCharacterCount,
    bool ProjectionWasTruncated,
    bool QuotedHistoryWasRemoved);
