namespace OD.MyLocalAgent.Core;

/// <summary>Extracts bounded structured text from supported Knowledge documents.</summary>
public interface IKnowledgeDocumentExtractor
{
    /// <summary>Extracts source-aware text blocks from a local administration path.</summary>
    Task<KnowledgeExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken);
}

/// <summary>Contains normalized document text and its source-aware blocks.</summary>
public sealed record KnowledgeExtractedDocument(
    string Text,
    IReadOnlyList<KnowledgeExtractedBlock> Blocks);

/// <summary>Maps one extracted text range to source metadata.</summary>
public sealed record KnowledgeExtractedBlock(
    int StartCharacter,
    int EndCharacter,
    string? Heading,
    int? PageNumber);
