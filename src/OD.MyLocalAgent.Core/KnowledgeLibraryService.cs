using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>Imports and administers the private embedded Knowledge library.</summary>
internal sealed class KnowledgeLibraryService : IKnowledgeLibraryService
{
    private readonly WorkspacePaths _paths;
    private readonly SqliteKnowledgeStore _store;
    private readonly IKnowledgeDocumentExtractor _extractor;
    private readonly KnowledgeChunker _chunker;
    private readonly IKnowledgeEmbeddingService _embeddingService;
    private readonly IOptionsMonitor<KnowledgeOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private bool _recoveryCompleted;

    /// <summary>Initializes the Knowledge administration service.</summary>
    public KnowledgeLibraryService(
        WorkspacePaths paths,
        SqliteKnowledgeStore store,
        IKnowledgeDocumentExtractor extractor,
        KnowledgeChunker chunker,
        IKnowledgeEmbeddingService embeddingService,
        IOptionsMonitor<KnowledgeOptions> options,
        TimeProvider timeProvider)
    {
        _paths = paths;
        _store = store;
        _extractor = extractor;
        _chunker = chunker;
        _embeddingService = embeddingService;
        _options = options;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_recoveryCompleted)
            {
                return;
            }
            foreach (var path in Directory.EnumerateFiles(_paths.KnowledgeStagingDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDelete(path);
            }

            foreach (var canonicalFileName in await _store.GetPendingFileCleanupAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CleanupCanonicalAsync(canonicalFileName, cancellationToken).ConfigureAwait(false);
            }

            var referencedFiles = await _store.GetReferencedCanonicalFilesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var path in Directory.EnumerateFiles(_paths.KnowledgeDocumentsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var canonicalFileName = Path.GetFileName(path);
                if (!referencedFiles.Contains(canonicalFileName))
                {
                    await _store.QueueFileCleanupAsync(canonicalFileName, cancellationToken).ConfigureAwait(false);
                    await CleanupCanonicalAsync(canonicalFileName, cancellationToken).ConfigureAwait(false);
                }
            }

            _recoveryCompleted = true;
        }
        finally
        {
            _writer.Release();
        }
    }

    /// <inheritdoc />
    public async Task<KnowledgeDocument> ImportAsync(string sourcePath, string name, string description, IReadOnlyList<string> tags, CancellationToken cancellationToken, bool allowDuplicate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tags);
        var normalizedTags = KnowledgeTagPolicy.NormalizeScope(tags);
        if (normalizedTags.Count == 0)
        {
            throw new ArgumentException("An imported Knowledge document requires at least one tag.", nameof(tags));
        }

        var source = new FileInfo(Path.GetFullPath(sourcePath));
        if (!source.Exists)
        {
            throw new FileNotFoundException("The selected Knowledge source file does not exist.", sourcePath);
        }

        if (source.Length > _options.CurrentValue.MaximumImportBytes)
        {
            throw new InvalidOperationException("The selected document exceeds the configured import limit.");
        }

        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? staging = null;
        string? canonical = null;
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var options = _options.CurrentValue;
            await _store.EnsureConfiguredContractAsync(options, cancellationToken).ConfigureAwait(false);
            var documentId = Guid.NewGuid().ToString("N");
            var extension = source.Extension.ToLowerInvariant();
            ValidateExtension(extension);
            staging = Path.Combine(_paths.KnowledgeStagingDirectory, $"{Guid.NewGuid():N}{extension}");
            await using (var input = source.OpenRead())
            await using (var output = new FileStream(
                staging,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            var sourceHash = await ComputeSha256Async(staging, cancellationToken).ConfigureAwait(false);
            var duplicate = await _store.FindActiveBySourceHashAsync(sourceHash, cancellationToken).ConfigureAwait(false);
            if (duplicate is not null && !allowDuplicate)
            {
                throw new KnowledgeDuplicateException(duplicate.Id, duplicate.Name);
            }
            var extracted = await _extractor.ExtractAsync(staging, cancellationToken).ConfigureAwait(false);
            var text = extracted.Text;
            var revisionId = Guid.NewGuid().ToString("N");
            var chunks = _chunker.Chunk(extracted).Select(chunk => chunk with { Id = $"{revisionId}:{chunk.Ordinal:D8}" }).ToArray();
            var embeddingInputs = chunks.Select(chunk => string.IsNullOrWhiteSpace(chunk.Heading) ? chunk.Text : $"{chunk.Heading}\n\n{chunk.Text}").ToArray();
            var vectors = await _embeddingService.GenerateAsync(embeddingInputs, cancellationToken).ConfigureAwait(false);
            canonical = Path.Combine(_paths.KnowledgeDocumentsDirectory, $"{documentId}{extension}");
            File.Move(staging, canonical);
            staging = null;
            var document = new KnowledgeDocument(documentId, name.Trim(), description.Trim(), normalizedTags, source.Name, GetMediaType(extension), source.Length, text.Length, chunks.Length, _timeProvider.GetUtcNow());
            var textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
            await _store.CommitAsync(document, Path.GetFileName(canonical), chunks, vectors, options, cancellationToken, revisionId, sourceHash, textHash).ConfigureAwait(false);
            canonical = null;
            return document;
        }
        finally
        {
            if (staging is not null)
            {
                TryDelete(staging);
            }

            if (canonical is not null && !TryDelete(canonical))
            {
                await _store.QueueFileCleanupAsync(Path.GetFileName(canonical), CancellationToken.None).ConfigureAwait(false);
            }

            _writer.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeDocument>> GetDocumentsAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await _store.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetTagCatalogAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await _store.GetTagCatalogAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunkPreview>> GetChunkPreviewAsync(string documentId, int maximumChunks, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (maximumChunks is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumChunks));
        }
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await _store.GetChunkPreviewAsync(documentId, maximumChunks, 20000, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<KnowledgeIndexStatus> GetIndexStatusAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await _store.GetIndexStatusAsync(_options.CurrentValue, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<KnowledgeDocument> UpdateDetailsAsync(string documentId, string name, string description, IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedTags = KnowledgeTagPolicy.NormalizeScope(tags);
        if (normalizedTags.Count == 0)
        {
            throw new ArgumentException("A Knowledge document requires at least one authorization tag.", nameof(tags));
        }
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return await _store.UpdateDetailsAsync(documentId, name.Trim(), description.Trim(), normalizedTags, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writer.Release();
        }
    }

    /// <inheritdoc />
    public async Task<KnowledgeDocument> ReplaceAsync(string documentId, string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var source = new FileInfo(Path.GetFullPath(sourcePath));
        if (!source.Exists)
        {
            throw new FileNotFoundException("The selected Knowledge source file does not exist.", sourcePath);
        }
        var options = _options.CurrentValue;
        if (source.Length > options.MaximumImportBytes)
        {
            throw new InvalidOperationException("The selected document exceeds the configured import limit.");
        }

        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? staging = null;
        string? newCanonicalPath = null;
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await _store.EnsureConfiguredContractAsync(options, cancellationToken).ConfigureAwait(false);
            var stored = await _store.GetStoredDocumentAsync(documentId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The Knowledge document no longer exists.");
            var extension = source.Extension.ToLowerInvariant();
            ValidateExtension(extension);
            staging = Path.Combine(_paths.KnowledgeStagingDirectory, $"{Guid.NewGuid():N}{extension}");
            await CopyAsync(source.FullName, staging, cancellationToken).ConfigureAwait(false);
            var canonicalName = $"{Guid.NewGuid():N}{extension}";
            var prepared = await PrepareAsync(staging, stored.Document with
            {
                OriginalFileName = source.Name,
                MediaType = GetMediaType(extension),
                SizeBytes = source.Length,
                UpdatedUtc = _timeProvider.GetUtcNow(),
            }, canonicalName, cancellationToken).ConfigureAwait(false);
            newCanonicalPath = Path.Combine(_paths.KnowledgeDocumentsDirectory, canonicalName);
            File.Move(staging, newCanonicalPath);
            staging = null;
            var oldCanonical = await _store.ReplaceAsync(prepared, options, cancellationToken).ConfigureAwait(false);
            newCanonicalPath = null;
            if (!string.Equals(oldCanonical, canonicalName, StringComparison.OrdinalIgnoreCase))
            {
                await CleanupCanonicalAsync(oldCanonical, CancellationToken.None).ConfigureAwait(false);
            }

            return prepared.Document;
        }
        finally
        {
            if (staging is not null)
            {
                TryDelete(staging);
            }
            if (newCanonicalPath is not null && !TryDelete(newCanonicalPath))
            {
                await _store.QueueFileCleanupAsync(Path.GetFileName(newCanonicalPath), CancellationToken.None).ConfigureAwait(false);
            }
            _writer.Release();
        }
    }

    /// <inheritdoc />
    public async Task<KnowledgeDocument> ReindexDocumentAsync(string documentId, IProgress<KnowledgeIndexProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var options = _options.CurrentValue;
            await _store.EnsureConfiguredContractAsync(options, cancellationToken).ConfigureAwait(false);
            var stored = await _store.GetStoredDocumentAsync(documentId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The Knowledge document no longer exists.");
            progress?.Report(new KnowledgeIndexProgress("Extracting", 0, 1, 0, 0));
            var path = Path.Combine(_paths.KnowledgeDocumentsDirectory, stored.CanonicalFileName);
            var prepared = await PrepareAsync(path, stored.Document with { UpdatedUtc = _timeProvider.GetUtcNow() }, stored.CanonicalFileName, cancellationToken, progress, 0, 1).ConfigureAwait(false);
            await _store.ReplaceAsync(prepared, options, cancellationToken).ConfigureAwait(false);
            progress?.Report(new KnowledgeIndexProgress("Complete", 1, 1, prepared.Vectors.Count, prepared.Vectors.Count));
            return prepared.Document;
        }
        finally
        {
            _writer.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReindexLibraryAsync(IProgress<KnowledgeIndexProgress>? progress, CancellationToken cancellationToken)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var documents = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
            if (documents.Count == 0)
            {
                return;
            }
            var prepared = new List<KnowledgePreparedIndex>(documents.Count);
            var totalEmbeddings = 0;
            for (var index = 0; index < documents.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stored = await _store.GetStoredDocumentAsync(documents[index].Id, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The Knowledge library changed while reindexing.");
                progress?.Report(new KnowledgeIndexProgress("Preparing", index, documents.Count, totalEmbeddings, 0));
                var path = Path.Combine(_paths.KnowledgeDocumentsDirectory, stored.CanonicalFileName);
                var item = await PrepareAsync(path, stored.Document with { UpdatedUtc = _timeProvider.GetUtcNow() }, stored.CanonicalFileName, cancellationToken, progress, index, documents.Count).ConfigureAwait(false);
                prepared.Add(item);
                totalEmbeddings += item.Vectors.Count;
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new KnowledgeIndexProgress("Activating", documents.Count, documents.Count, totalEmbeddings, totalEmbeddings));
            await _store.ReplaceAllIndexesAsync(prepared, _options.CurrentValue, cancellationToken).ConfigureAwait(false);
            progress?.Report(new KnowledgeIndexProgress("Complete", documents.Count, documents.Count, totalEmbeddings, totalEmbeddings));
        }
        finally
        {
            _writer.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _store.DeleteAsync(documentId, cancellationToken).ConfigureAwait(false);
            if (result.Deleted && result.CanonicalFile is not null)
            {
                await CleanupCanonicalAsync(result.CanonicalFile, CancellationToken.None).ConfigureAwait(false);
            }

            return result.Deleted;
        }
        finally
        {
            _writer.Release();
        }
    }

    private static string GetMediaType(string extension) => extension switch { ".txt" => "text/plain", ".md" or ".markdown" => "text/markdown", ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".pdf" => "application/pdf", _ => "application/octet-stream" };
    private async Task<KnowledgePreparedIndex> PrepareAsync(string path, KnowledgeDocument document, string canonicalFileName, CancellationToken cancellationToken, IProgress<KnowledgeIndexProgress>? progress = null, int completedDocuments = 0, int totalDocuments = 1)
    {
        var extracted = await _extractor.ExtractAsync(path, cancellationToken).ConfigureAwait(false);
        var text = extracted.Text;
        progress?.Report(new KnowledgeIndexProgress("Chunking", completedDocuments, totalDocuments, 0, 0));
        var revisionId = Guid.NewGuid().ToString("N");
        var chunks = _chunker.Chunk(extracted).Select(chunk => chunk with { Id = $"{revisionId}:{chunk.Ordinal:D8}" }).ToArray();
        var inputs = chunks.Select(chunk => string.IsNullOrWhiteSpace(chunk.Heading) ? chunk.Text : $"{chunk.Heading}\n\n{chunk.Text}").ToArray();
        progress?.Report(new KnowledgeIndexProgress("Embedding", completedDocuments, totalDocuments, 0, chunks.Length));
        var vectors = await _embeddingService.GenerateAsync(inputs, cancellationToken).ConfigureAwait(false);
        var indexed = document with { CharacterCount = text.Length, ChunkCount = chunks.Length };
        progress?.Report(new KnowledgeIndexProgress("Embedding", completedDocuments, totalDocuments, vectors.Count, vectors.Count));
        var sourceHash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        var textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new KnowledgePreparedIndex(indexed, canonicalFileName, chunks, vectors, revisionId, sourceHash, textHash);
    }
    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    private static void ValidateExtension(string extension)
    {
        if (extension is not (".txt" or ".md" or ".markdown" or ".docx" or ".pdf"))
        {
            throw new InvalidOperationException(
                "Knowledge supports TXT, Markdown, DOCX, and text-based PDF files.");
        }
    }
    private async Task CleanupCanonicalAsync(string canonicalFileName, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetFileName(canonicalFileName), canonicalFileName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(canonicalFileName))
        {
            throw new InvalidOperationException("Knowledge cleanup contains an invalid canonical filename.");
        }

        var path = Path.Combine(_paths.KnowledgeDocumentsDirectory, canonicalFileName);
        if (TryDelete(path))
        {
            await _store.CompleteFileCleanupAsync(canonicalFileName, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
