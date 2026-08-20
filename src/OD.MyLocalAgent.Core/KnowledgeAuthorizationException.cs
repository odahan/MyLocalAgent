namespace OD.MyLocalAgent.Core;

/// <summary>Represents a denied Knowledge scope before embedding or database access.</summary>
public sealed class KnowledgeAuthorizationException : Exception
{
    /// <summary>Initializes a new instance of the exception.</summary>
    public KnowledgeAuthorizationException(string message)
        : base(message)
    {
    }
}
