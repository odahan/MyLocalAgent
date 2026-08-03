namespace OD.MyLocalAgent.Core;

/// <summary>
/// Carries a deterministic media-aware HTTP projection and omission metadata.
/// </summary>
/// <param name="Content">The bounded projected content.</param>
/// <param name="OriginalCharacterCount">The fetched response character count.</param>
/// <param name="ProjectedCharacterCount">The delivered projection character count.</param>
/// <param name="OmittedCharacterCount">The omitted projected-source character count.</param>
/// <param name="ProjectionWasTruncated">Whether the readable projection was capped.</param>
public sealed record HttpContentProjection(
    string Content,
    int OriginalCharacterCount,
    int ProjectedCharacterCount,
    int OmittedCharacterCount,
    bool ProjectionWasTruncated);
