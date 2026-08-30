using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace OD.MyLocalAgent.Core;

/// <summary>Extracts bounded text from supported Knowledge document formats without OCR.</summary>
public sealed class KnowledgeDocumentExtractor : IKnowledgeDocumentExtractor
{
    private readonly IOptionsMonitor<KnowledgeOptions> _options;

    /// <summary>Initializes the extractor.</summary>
    public KnowledgeDocumentExtractor(IOptionsMonitor<KnowledgeOptions> options) => _options = options;

    /// <inheritdoc />
    public Task<KnowledgeExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => Extract(path, cancellationToken), cancellationToken);

    private KnowledgeExtractedDocument Extract(string path, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var extracted = extension switch
        {
            ".txt" or ".md" or ".markdown" => ExtractPlainText(path),
            ".html" or ".htm" => ExtractHtml(path),
            ".docx" => ExtractDocx(path, cancellationToken),
            ".pdf" => ExtractPdf(path, cancellationToken),
            _ => throw new InvalidOperationException("Knowledge supports TXT, Markdown, HTML, DOCX, and text-based PDF files."),
        };
        var text = extracted.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("The document contains no extractable text. OCR is not supported.");
        }

        if (text.Length > _options.CurrentValue.MaximumExtractedCharacters)
        {
            throw new InvalidOperationException("The extracted document exceeds the configured character limit.");
        }

        return extracted;
    }

    private static KnowledgeExtractedDocument ExtractPlainText(string path)
    {
        var text = FileContentValidation.NormalizeText(
            File.ReadAllText(
                path,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)));
        return new KnowledgeExtractedDocument(text, [new KnowledgeExtractedBlock(0, text.Length, null, null)]);
    }

    private static KnowledgeExtractedDocument ExtractHtml(string path)
    {
        var html = File.ReadAllText(
            path,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true));
        var markdown = FileContentValidation.NormalizeText(
            HtmlDocumentConverter.Convert(html, HtmlTextFormat.Markdown));
        return new KnowledgeExtractedDocument(
            markdown,
            [new KnowledgeExtractedBlock(0, markdown.Length, null, null)]);
    }

    private static KnowledgeExtractedDocument ExtractDocx(string path, CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Open(path, false);
        if (document.DocumentType != WordprocessingDocumentType.Document ||
            document.MainDocumentPart?.ExternalRelationships.Any() == true ||
            document.ExternalRelationships.Any())
        {
            throw new InvalidOperationException("The DOCX file is unreadable or contains unsupported external content.");
        }

        var result = DocumentTextExtraction.ExtractDocxBody(document, cancellationToken);
        return new KnowledgeExtractedDocument(
            result.Text,
            [new KnowledgeExtractedBlock(0, result.Text.Length, null, null)]);
    }

    private static KnowledgeExtractedDocument ExtractPdf(string path, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(path);
        if (document.IsEncrypted)
        {
            throw new InvalidOperationException("Encrypted PDF documents are not supported.");
        }

        var text = new StringBuilder();
        var blocks = new List<KnowledgeExtractedBlock>(document.NumberOfPages);
        for (var page = 1; page <= document.NumberOfPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (text.Length > 0)
            {
                text.Append("\n\n");
            }

            var start = text.Length;
            text.Append(DocumentTextExtraction.ExtractPdfPage(document, page));
            blocks.Add(new KnowledgeExtractedBlock(start, text.Length, null, page));
        }

        var normalized = FileContentValidation.NormalizeText(text.ToString());
        return new KnowledgeExtractedDocument(normalized, blocks);
    }
}
