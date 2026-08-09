namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents one normalized search hit with stable hit metadata.
/// </summary>
/// <param name="Id">The stable hit identifier.</param>
/// <param name="Rank">The provider-assigned rank (1-based).</param>
/// <param name="Url">The source URL.</param>
/// <param name="Domain">The domain of the URL.</param>
/// <param name="ProtectedContentIndex">The index into the protected content array.</param>
public sealed record SearchHit(
    string Id,
    int Rank,
    string Url,
    string Domain,
    int ProtectedContentIndex);
