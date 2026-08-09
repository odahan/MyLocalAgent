using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>Implements bounded, read-only DOCX body text extraction.</summary>
[ToolGroup("document", "Word documents", "Read bounded text from authorized DOCX document bodies.", 700, Activation = ToolActivationKind.Contextual, ServiceContract = typeof(IDocumentTool))]
public sealed class DocumentTool : IDocumentTool
{
    private static readonly string[] OmittedParts =
    [
        "headers", "footers", "comments", "footnotes", "endnotes", "textBoxes", "images",
        "altText", "embeddedPackages", "customXml", "externalRelationships"
    ];

    private readonly IPathSandbox _sandbox;
    private readonly IOptions<FileContentOptions> _options;
    private readonly ToolExecutionContext _context;

    /// <summary>Initializes a new instance of the <see cref="DocumentTool"/> class.</summary>
    public DocumentTool(IPathSandbox sandbox, IOptions<FileContentOptions> options, ToolExecutionContext context)
    {
        _sandbox = sandbox;
        _options = options;
        _context = context;
    }

    /// <inheritdoc/>
    [ToolLocalPath("documentPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("document.readDocx", "Reads bounded text from an authorized DOCX document body.", 100, AgentFunctionName = "document_read_docx")]
    public Task<string> ReadDocxAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized DOCX file path.", Example = "documents/notes.docx")] string documentPath,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum extracted characters.", Example = "2000")] int? maxCharacters = null,
        CancellationToken cancellationToken = default) => Task.Run(() => Read(documentPath, maxCharacters, cancellationToken), cancellationToken);

    private string Read(string documentPath, int? maxCharacters, CancellationToken cancellationToken)
    {
        var path = FileContentValidation.ResolveFile(_sandbox, documentPath, _context, _options.Value);
        if (!path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) throw new FileContentToolException("UnsupportedFormat");
        var limit = FileContentValidation.Limit(maxCharacters, _options.Value.MaxDocumentCharacters);

        try
        {
            using var word = WordprocessingDocument.Open(path, false);
            if (word.DocumentType != WordprocessingDocumentType.Document) throw new FileContentToolException("FormatMismatch");
            if (word.MainDocumentPart?.ExternalRelationships.Any() == true || word.ExternalRelationships.Any())
                throw new FileContentToolException("FormatMismatch");

            var body = word.MainDocumentPart?.Document?.Body;
            if (body is null) return Serialize(string.Empty, 0, 0, 0, false);

            var blocks = new List<string>();
            var paragraphCount = 0;
            var tableCount = 0;
            foreach (var child in body.ChildElements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (child)
                {
                    case Paragraph paragraph:
                        blocks.Add(ReadInline(paragraph));
                        paragraphCount++;
                        break;
                    case Table table:
                        blocks.Add(ReadTable(table, ref paragraphCount));
                        tableCount++;
                        break;
                }
            }

            var original = FileContentValidation.NormalizeText(string.Join('\n', blocks));
            var delivered = FileContentValidation.TruncateAtScalarBoundary(original, limit);
            return Serialize(delivered, original.Length, paragraphCount, tableCount, delivered.Length < original.Length);
        }
        catch (FileContentToolException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (OpenXmlPackageException) { throw new FileContentToolException("CorruptFile"); }
        catch (InvalidDataException) { throw new FileContentToolException("CorruptFile"); }
        catch { throw new FileContentToolException("CorruptFile"); }
    }

    private static string ReadTable(Table table, ref int paragraphCount)
    {
        var rows = new List<string>();
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<TableCell>())
            {
                var paragraphs = cell.Elements<Paragraph>().ToArray();
                paragraphCount += paragraphs.Length;
                cells.Add(string.Join('\n', paragraphs.Select(ReadInline)));
            }
            rows.Add(string.Join('\t', cells));
        }
        return string.Join('\n', rows);
    }

    private static string ReadInline(OpenXmlElement container)
    {
        var output = new StringBuilder();
        AppendInline(container, output);
        return output.ToString();
    }

    private static void AppendInline(OpenXmlElement element, StringBuilder output)
    {
        if (element is DeletedRun or MoveFromRun or DeletedText) return;
        switch (element)
        {
            case Text text:
                output.Append(text.Text);
                return;
            case TabChar:
                output.Append('\t');
                return;
            case Break or CarriageReturn:
                output.Append('\n');
                return;
        }
        foreach (var child in element.ChildElements) AppendInline(child, output);
    }

    private static string Serialize(string text, int originalCount, int paragraphCount, int tableCount, bool truncated) =>
        ToolJson.Serialize(new
        {
            format = "docx",
            text,
            paragraphCount,
            tableCount,
            originalCharacterCount = originalCount,
            deliveredCharacterCount = text.Length,
            truncated,
            omittedParts = OmittedParts,
            untrustedContent = true
        });
}
