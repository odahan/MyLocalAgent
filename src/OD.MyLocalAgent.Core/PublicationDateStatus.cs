namespace OD.MyLocalAgent.Core;

/// <summary>
/// Indicates the verification status of a document's publication date.
/// </summary>
public enum PublicationDateStatus
{
    /// <summary>The publication date was verified from metadata.</summary>
    Verified,

    /// <summary>The publication date is unknown.</summary>
    Unknown,
}
