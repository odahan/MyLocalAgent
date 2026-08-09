using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using SpreadsheetText = DocumentFormat.OpenXml.Spreadsheet.Text;
using WordRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;
using PdfPageSize = UglyToad.PdfPig.Content.PageSize;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies Stage 22 read-only Office tools with synthetic documents.</summary>
public sealed class Stage22OfficeToolTests : IDisposable
{
    private readonly string _root;
    private readonly IPathSandbox _sandbox;
    private readonly IOptions<FileContentOptions> _options;

    /// <summary>Initializes an isolated Office test root.</summary>
    public Stage22OfficeToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Stage22.Office", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _sandbox = new PathSandbox(Options.Create(new FileSystemOptions { RootPath = _root }));
        _options = Options.Create(new FileContentOptions());
    }

    /// <summary>Verifies DOCX body text is returned under the untrusted boundary.</summary>
    [Fact]
    public async Task ReadDocxReturnsBoundedBodyText()
    {
        var path = Path.Combine(_root, "sample.docx");
        using (var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new WordRun(new WordText("Hello"))),
                new Paragraph(new WordRun(new WordText("world")))));
            main.Document.Save();
        }

        var tool = new DocumentTool(_sandbox, _options, ToolExecutionContext.Taskless);
        using var result = JsonDocument.Parse(await tool.ReadDocxAsync("sample.docx"));
        result.RootElement.GetProperty("text").GetString().Should().Contain("Hello").And.Contain("world");
        result.RootElement.GetProperty("untrustedContent").GetBoolean().Should().BeTrue();
    }

    /// <summary>Verifies XLSX sheet listing and one-based bounded range reading.</summary>
    [Fact]
    public async Task SpreadsheetListsSheetsAndReadsStoredValues()
    {
        CreateWorkbook(Path.Combine(_root, "sample.xlsx"));
        var tool = new SpreadsheetTool(_sandbox, _options, ToolExecutionContext.Taskless);

        using var listed = JsonDocument.Parse(await tool.ListSheetsAsync("sample.xlsx"));
        listed.RootElement.GetProperty("totalSheetCount").GetInt32().Should().Be(1);
        listed.RootElement.GetProperty("sheets")[0].GetProperty("name").GetString().Should().Be("Data");

        using var range = JsonDocument.Parse(await tool.ReadRangeAsync("sample.xlsx", "Data", 1, 1, 2, 2));
        range.RootElement.GetProperty("rows")[0].GetProperty("cells")[0].GetProperty("value").GetString().Should().Be("Name");
        range.RootElement.GetProperty("rows")[1].GetProperty("cells")[1].GetProperty("value").GetDouble().Should().Be(42);
        range.RootElement.GetProperty("untrustedContent").GetBoolean().Should().BeTrue();

        var boundedTool = new SpreadsheetTool(
            _sandbox,
            Options.Create(new FileContentOptions { MaxDocumentCharacters = 4096 }),
            ToolExecutionContext.Taskless);
        using var bounded = JsonDocument.Parse(await boundedTool.ReadRangeAsync("sample.xlsx", "Data", 3, 1, 1, 1));
        bounded.RootElement.GetProperty("rows")[0].GetProperty("cells")[0].GetProperty("cellType").GetString().Should().Be("omitted");
        bounded.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
    }

    /// <summary>Verifies PDF metadata and page-oriented layout text extraction.</summary>
    [Fact]
    public async Task PdfReturnsMetadataAndSelectedPages()
    {
        var builder = new PdfDocumentBuilder
        {
            IncludeDocumentInformation = true,
            DocumentInformation = new PdfDocumentBuilder.DocumentInformationBuilder { Title = "Synthetic report", Author = "Stage 22" }
        };
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PdfPageSize.A4).AddText("First page", 12, new PdfPoint(50, 750), font);
        builder.AddPage(PdfPageSize.A4).AddText("Second page", 12, new PdfPoint(50, 750), font);
        builder.AddPage(PdfPageSize.A4);
        await File.WriteAllBytesAsync(Path.Combine(_root, "sample.pdf"), builder.Build());
        builder.Dispose();

        var tool = new PdfTool(_sandbox, _options, ToolExecutionContext.Taskless);
        using var info = JsonDocument.Parse(await tool.GetInfoAsync("sample.pdf"));
        info.RootElement.GetProperty("format").GetString().Should().Be("pdf");
        info.RootElement.GetProperty("pageCount").GetInt32().Should().Be(3);
        info.RootElement.GetProperty("title").GetString().Should().Be("Synthetic report");

        using var text = JsonDocument.Parse(await tool.ReadTextAsync("sample.pdf", 2, 1, 100));
        text.RootElement.GetProperty("deliveredPageCount").GetInt32().Should().Be(1);
        text.RootElement.GetProperty("pages")[0].GetProperty("pageNumber").GetInt32().Should().Be(2);
        text.RootElement.GetProperty("pages")[0].GetProperty("text").GetString().Should().Contain("Second page");
        text.RootElement.GetProperty("hasMorePages").GetBoolean().Should().BeTrue();

        using var empty = JsonDocument.Parse(await tool.ReadTextAsync("sample.pdf", 3, 1, 100));
        empty.RootElement.GetProperty("pages")[0].GetProperty("text").GetString().Should().BeEmpty();
        empty.RootElement.GetProperty("pages")[0].GetProperty("hasExtractableText").GetBoolean().Should().BeFalse();
    }

    private static void CreateWorkbook(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData(
            new Row(
                new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new SpreadsheetText("Name")) },
                new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new SpreadsheetText("Value")) }),
            new Row(
                new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new SpreadsheetText("Answer")) },
                new Cell { CellValue = new CellValue("42"), DataType = CellValues.Number }),
            new Row(
                new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new SpreadsheetText(new string('x', 5000))) })));
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Data" });
        workbookPart.Workbook.Save();
    }

    /// <summary>Removes the isolated test root.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
