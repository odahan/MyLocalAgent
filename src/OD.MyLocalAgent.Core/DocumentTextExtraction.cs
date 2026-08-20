using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OD.MyLocalAgent.Core;

/// <summary>Provides shared DOCX and PDF text extraction primitives.</summary>
internal static class DocumentTextExtraction
{
    /// <summary>Extracts the body of an already validated Word document.</summary>
    public static DocxExtractionResult ExtractDocxBody(
        WordprocessingDocument document,
        CancellationToken cancellationToken)
    {
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return new DocxExtractionResult(string.Empty, 0, 0);
        }

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

        return new DocxExtractionResult(
            FileContentValidation.NormalizeText(string.Join('\n', blocks)),
            paragraphCount,
            tableCount);
    }

    /// <summary>Extracts and normalizes one page from an already opened PDF.</summary>
    public static string ExtractPdfPage(PdfDocument document, int pageNumber) =>
        FileContentValidation.NormalizeText(
            ContentOrderTextExtractor.GetText(document.GetPage(pageNumber)));

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
        if (element is DeletedRun or MoveFromRun or DeletedText)
        {
            return;
        }

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

        foreach (var child in element.ChildElements)
        {
            AppendInline(child, output);
        }
    }
}

internal sealed record DocxExtractionResult(string Text, int ParagraphCount, int TableCount);
