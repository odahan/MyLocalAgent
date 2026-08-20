namespace OD.MyLocalAgent.Core;

/// <summary>Administers imported documents in the embedded Knowledge library.</summary>
public interface IKnowledgeLibraryService
{
    /// <summary>Initializes the Knowledge store.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);
    /// <summary>Imports and indexes one external document into private application storage.</summary>
    Task<KnowledgeDocument> ImportAsync(string sourcePath, string name, string description, IReadOnlyList<string> tags, CancellationToken cancellationToken, bool allowDuplicate = false);
    /// <summary>Replaces the active content of an existing document atomically.</summary>
    Task<KnowledgeDocument> ReplaceAsync(string documentId, string sourcePath, CancellationToken cancellationToken);
    /// <summary>Updates administrative details without recomputing embeddings.</summary>
    Task<KnowledgeDocument> UpdateDetailsAsync(string documentId, string name, string description, IReadOnlyList<string> tags, CancellationToken cancellationToken);
    /// <summary>Rebuilds one document with the active index contract.</summary>
    Task<KnowledgeDocument> ReindexDocumentAsync(string documentId, IProgress<KnowledgeIndexProgress>? progress, CancellationToken cancellationToken);
    /// <summary>Rebuilds and atomically activates the complete library under the configured contract.</summary>
    Task ReindexLibraryAsync(IProgress<KnowledgeIndexProgress>? progress, CancellationToken cancellationToken);
    /// <summary>Returns the configured and persisted index-contract state.</summary>
    Task<KnowledgeIndexStatus> GetIndexStatusAsync(CancellationToken cancellationToken);
    /// <summary>Returns current Knowledge documents.</summary>
    Task<IReadOnlyList<KnowledgeDocument>> GetDocumentsAsync(CancellationToken cancellationToken);
    /// <summary>Returns the current normalized authorization-tag catalog.</summary>
    Task<IReadOnlyList<string>> GetTagCatalogAsync(CancellationToken cancellationToken);
    /// <summary>Returns a bounded preview of active chunks for extraction diagnostics.</summary>
    Task<IReadOnlyList<KnowledgeChunkPreview>> GetChunkPreviewAsync(string documentId, int maximumChunks, CancellationToken cancellationToken);
    /// <summary>Deletes one document from the searchable library.</summary>
    Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken);
}
