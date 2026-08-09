namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents a discovered web document with stable metadata.
/// </summary>
/// <param name="HitId">The stable search hit identifier.</param>
/// <param name="SourceUrl">The original source URL.</param>
/// <param name="FinalUrl">The final URL after redirects.</param>
/// <param name="CanonicalUrl">The normalized canonical URL.</param>
/// <param name="Title">The document title.</param>
/// <param name="Domain">The document domain.</param>
/// <param name="Kind">The document type classification.</param>
/// <param name="PublicationTime">The publication date-time, or null when unknown.</param>
/// <param name="DateStatus">The publication date verification status.</param>
/// <param name="ShortSummary">A compact text summary of the document content.</param>
/// <param name="ContentType">The HTTP content type of the fetched page.</param>
/// <param name="TrustScore">The Guardrails trust score for the content.</param>
/// <param name="TrustDisposition">The Guardrails trust disposition.</param>
/// <param name="DiscoveryQueryIds">The query identifiers that discovered this document.</param>
/// <param name="RetrievalTimeUtc">The UTC time when the document was retrieved.</param>
public sealed record WebDocument(
    string HitId,
    string SourceUrl,
    string FinalUrl,
    string CanonicalUrl,
    string Title,
    string Domain,
    WebDocumentKind Kind,
    DateTimeOffset? PublicationTime,
    PublicationDateStatus DateStatus,
    string ShortSummary,
    string ContentType,
    int TrustScore,
    string TrustDisposition,
    IReadOnlyList<string> DiscoveryQueryIds,
    DateTimeOffset RetrievalTimeUtc);
