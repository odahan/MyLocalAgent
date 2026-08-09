namespace OD.MyLocalAgent.Core;

/// <summary>
/// Categorizes the type of a discovered web document.
/// </summary>
public enum WebDocumentKind
{
    /// <summary>A news or blog article.</summary>
    Article,

    /// <summary>A social media or forum post.</summary>
    Post,

    /// <summary>Technical documentation.</summary>
    Documentation,

    /// <summary>A release note or changelog.</summary>
    ReleaseNote,

    /// <summary>A source code repository.</summary>
    Repository,

    /// <summary>A video page.</summary>
    Video,

    /// <summary>An unclassified document type.</summary>
    Other,
}
