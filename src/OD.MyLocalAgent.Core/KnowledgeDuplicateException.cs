namespace OD.MyLocalAgent.Core;

/// <summary>Reports an exact imported-content duplicate requiring an explicit user decision.</summary>
public sealed class KnowledgeDuplicateException : InvalidOperationException
{
    /// <summary>Initializes duplicate-content details.</summary>
    public KnowledgeDuplicateException(string existingDocumentId, string existingDocumentName)
        : base($"The same content is already imported as Knowledge document '{existingDocumentName}'.")
    {
        ExistingDocumentId = existingDocumentId;
        ExistingDocumentName = existingDocumentName;
    }

    /// <summary>Gets the existing document identifier.</summary>
    public string ExistingDocumentId { get; }

    /// <summary>Gets the existing document display name.</summary>
    public string ExistingDocumentName { get; }
}
