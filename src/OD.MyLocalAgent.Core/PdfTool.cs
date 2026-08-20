using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace OD.MyLocalAgent.Core;

/// <summary>Implements bounded, layout-aware read-only PDF text operations.</summary>
[ToolGroup("pdf", "PDF documents", "Inspect and extract bounded text from authorized PDF documents without OCR.", 900, Activation = ToolActivationKind.Contextual, ServiceContract = typeof(IPdfTool))]
public sealed class PdfTool : IPdfTool
{
    private const string TruncationMarker = "\n[truncated]";
    private readonly IPathSandbox _sandbox;
    private readonly IOptions<FileContentOptions> _options;
    private readonly ToolExecutionContext _context;

    /// <summary>Initializes a new instance of the <see cref="PdfTool"/> class.</summary>
    public PdfTool(IPathSandbox sandbox, IOptions<FileContentOptions> options, ToolExecutionContext context)
    {
        _sandbox = sandbox;
        _options = options;
        _context = context;
    }

    /// <inheritdoc/>
    [ToolLocalPath("pdfPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("pdf.getInfo", "Reads bounded non-content information from an authorized PDF.", 100, AgentFunctionName = "pdf_get_info")]
    public Task<string> GetInfoAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized PDF file path.", Example = "documents/report.pdf")] string pdfPath,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var document = Open(pdfPath);
        cancellationToken.ThrowIfCancellationRequested();
        var remaining = _options.Value.MaxDocumentCharacters;
        string? Bounded(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var normalized = FileContentValidation.NormalizeText(value);
            var delivered = FileContentValidation.TruncateAtScalarBoundary(normalized, remaining);
            remaining -= delivered.Length;
            return delivered;
        }
        var information = document.Information;
        return ToolJson.Serialize(new
        {
            format = "pdf",
            pageCount = document.NumberOfPages,
            pdfVersion = document.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            encrypted = false,
            title = Bounded(information.Title),
            author = Bounded(information.Author),
            subject = Bounded(information.Subject),
            keywords = Bounded(information.Keywords),
            creator = Bounded(information.Creator),
            producer = Bounded(information.Producer),
            metadataTruncated = remaining == 0,
            untrustedContent = true
        });
    }, cancellationToken);

    /// <inheritdoc/>
    [ToolLocalPath("pdfPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("pdf.readText", "Extracts bounded layout-aware text from selected authorized PDF pages.", 200, AgentFunctionName = "pdf_read_text")]
    public Task<string> ReadTextAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized PDF file path.", Example = "documents/report.pdf")] string pdfPath,
        [ToolArgument(WorkflowArgumentKind.Integer, "One-based first page.", Example = "1")] int startPage = 1,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum pages.", Example = "5")] int? maxPages = null,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum characters.", Example = "4000")] int? maxCharacters = null,
        CancellationToken cancellationToken = default) => Task.Run(() => ReadText(pdfPath, startPage, maxPages, maxCharacters, cancellationToken), cancellationToken);

    private string ReadText(string pdfPath, int startPage, int? maxPages, int? maxCharacters, CancellationToken cancellationToken)
    {
        using var document = Open(pdfPath);
        if (startPage < 1 || startPage > document.NumberOfPages) throw new ArgumentOutOfRangeException(nameof(startPage));
        var pageLimit = FileContentValidation.Limit(maxPages, _options.Value.MaxPdfPages);
        var characterLimit = FileContentValidation.Limit(maxCharacters, _options.Value.MaxDocumentCharacters);
        var pages = new List<object>();
        var characterCount = 0;
        var truncated = false;
        var lastRequestedPage = Math.Min(document.NumberOfPages, checked(startPage + pageLimit - 1));
        for (var pageNumber = startPage; pageNumber <= lastRequestedPage; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text;
            try { text = DocumentTextExtraction.ExtractPdfPage(document, pageNumber); }
            catch { throw new FileContentToolException("CorruptFile"); }
            var hasExtractableText = !string.IsNullOrWhiteSpace(text);
            var available = characterLimit - characterCount;
            var pageTruncated = text.Length > available;
            if (pageTruncated)
            {
                var prefixLimit = Math.Max(0, available - TruncationMarker.Length);
                text = FileContentValidation.TruncateAtScalarBoundary(text, prefixLimit) + (available >= TruncationMarker.Length ? TruncationMarker : string.Empty);
                truncated = true;
            }
            pages.Add(new { pageNumber, text, hasExtractableText, truncated = pageTruncated });
            characterCount += text.Length;
            if (pageTruncated || characterCount >= characterLimit) break;
        }
        var deliveredPageCount = pages.Count;
        var hasMorePages = startPage + deliveredPageCount - 1 < document.NumberOfPages;
        return ToolJson.Serialize(new
        {
            format = "pdf",
            documentPageCount = document.NumberOfPages,
            startPage,
            requestedPageCount = pageLimit,
            deliveredPageCount,
            deliveredCharacterCount = characterCount,
            pages,
            hasMorePages,
            truncated = truncated || deliveredPageCount < Math.Min(pageLimit, document.NumberOfPages - startPage + 1),
            untrustedContent = true
        });
    }

    private PdfDocument Open(string requestedPath)
    {
        var path = FileContentValidation.ResolveFile(_sandbox, requestedPath, _context, _options.Value);
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new FileContentToolException("UnsupportedFormat");
        try
        {
            var document = PdfDocument.Open(path);
            if (document.IsEncrypted)
            {
                document.Dispose();
                throw new FileContentToolException("EncryptedFile");
            }
            return document;
        }
        catch (FileContentToolException) { throw; }
        catch { throw new FileContentToolException("CorruptFile"); }
    }
}
