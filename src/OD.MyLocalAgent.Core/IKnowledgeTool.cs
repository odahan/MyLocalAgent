namespace OD.MyLocalAgent.Core;

/// <summary>Exposes authorized semantic retrieval as an ordinary declarative tool.</summary>
public interface IKnowledgeTool
{
    /// <summary>Searches user-managed local documents within the execution tag scope.</summary>
    Task<string> SearchAsync(string query,string[]? tags=null,int? maxResults=null,CancellationToken cancellationToken=default);
}
