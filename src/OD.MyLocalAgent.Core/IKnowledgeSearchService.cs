namespace OD.MyLocalAgent.Core;

/// <summary>Searches only an explicitly authorized local Knowledge scope.</summary>
public interface IKnowledgeSearchService
{
    /// <summary>Searches semantically within the supplied owning and optional call scopes.</summary>
    Task<KnowledgeSearchResponse> SearchAsync(
        string query,
        IReadOnlyList<string> owningTags,
        IReadOnlyList<string>? requestedTags,
        int? maximumResults,
        CancellationToken cancellationToken);
}
