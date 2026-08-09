using System.Globalization;
using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>Implements streaming, read-only spreadsheet inspection.</summary>
[ToolGroup("spreadsheet", "Spreadsheets", "Inspect authorized XLS, XLSX, and XLSB workbook values without recalculation.", 800, Activation = ToolActivationKind.Contextual, ServiceContract = typeof(ISpreadsheetTool))]
public sealed class SpreadsheetTool : ISpreadsheetTool
{
    private readonly IPathSandbox _sandbox;
    private readonly IOptions<FileContentOptions> _options;
    private readonly ToolExecutionContext _context;

    static SpreadsheetTool() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Initializes a new instance of the <see cref="SpreadsheetTool"/> class.</summary>
    public SpreadsheetTool(IPathSandbox sandbox, IOptions<FileContentOptions> options, ToolExecutionContext context)
    {
        _sandbox = sandbox;
        _options = options;
        _context = context;
    }

    /// <inheritdoc/>
    [ToolLocalPath("workbookPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("spreadsheet.listSheets", "Lists authorized workbook sheets in their stored order.", 100, AgentFunctionName = "spreadsheet_list_sheets")]
    public Task<string> ListSheetsAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized XLS, XLSX, or XLSB workbook path.", Example = "data/report.xlsx")] string workbookPath,
        CancellationToken cancellationToken = default) => Task.Run(() => Sanitize(() =>
    {
        using var reader = Open(workbookPath);
        var sheets = new List<object>();
        var index = 1;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            sheets.Add(new { index, name = FileContentValidation.NormalizeText(reader.Name) });
            index++;
        } while (reader.NextResult());
        return ToolJson.Serialize(new { format = Format(workbookPath), sheets, totalSheetCount = sheets.Count, deliveredSheetCount = sheets.Count, untrustedContent = true });
    }), cancellationToken);

    /// <inheritdoc/>
    [ToolLocalPath("workbookPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("spreadsheet.readRange", "Reads a bounded one-based range of stored workbook values.", 200, AgentFunctionName = "spreadsheet_read_range")]
    public Task<string> ReadRangeAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized workbook path.", Example = "data/report.xlsx")] string workbookPath,
        [ToolArgument(WorkflowArgumentKind.String, "Optional exact sheet name.", Example = "Summary")] string? sheetName = null,
        [ToolArgument(WorkflowArgumentKind.Integer, "One-based first row.", Example = "1")] int startRow = 1,
        [ToolArgument(WorkflowArgumentKind.Integer, "One-based first column.", Example = "1")] int startColumn = 1,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum rows.", Example = "100")] int? maxRows = null,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum columns.", Example = "20")] int? maxColumns = null,
        CancellationToken cancellationToken = default) => Task.Run(
            () => Sanitize(() => ReadRange(workbookPath, sheetName, startRow, startColumn, maxRows, maxColumns, cancellationToken)),
            cancellationToken);

    private string ReadRange(string workbookPath, string? sheetName, int startRow, int startColumn, int? maxRows, int? maxColumns, CancellationToken cancellationToken)
    {
        if (startRow < 1) throw new ArgumentOutOfRangeException(nameof(startRow));
        if (startColumn < 1) throw new ArgumentOutOfRangeException(nameof(startColumn));
        var rowLimit = FileContentValidation.Limit(maxRows, _options.Value.MaxSpreadsheetRows);
        var columnLimit = FileContentValidation.Limit(maxColumns, _options.Value.MaxSpreadsheetColumns);
        using var reader = Open(workbookPath);
        var sheetIndex = 1;
        if (!string.IsNullOrEmpty(sheetName))
        {
            while (!string.Equals(reader.Name, sheetName, StringComparison.Ordinal) && reader.NextResult()) sheetIndex++;
            if (!string.Equals(reader.Name, sheetName, StringComparison.Ordinal)) throw new FileContentToolException("SheetNotFound");
        }

        var rows = new List<object>();
        var sourceRow = 0;
        var cellCount = 0;
        var characterCount = 0;
        var stoppedByLimit = false;
        var omittedByCharacterLimit = false;
        var hasMoreRows = false;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourceRow++;
            if (sourceRow < startRow) continue;
            if (rows.Count >= rowLimit || cellCount + columnLimit > _options.Value.MaxSpreadsheetCells)
            {
                stoppedByLimit = true;
                hasMoreRows = true;
                break;
            }

            var cells = new object[columnLimit];
            var rowCharacters = 0;
            for (var offset = 0; offset < columnLimit; offset++)
            {
                var sourceColumn = startColumn - 1 + offset;
                var cell = sourceColumn < reader.FieldCount ? ToCell(reader.GetValue(sourceColumn)) : ToCell(null);
                var remainingCharacters = _options.Value.MaxDocumentCharacters - characterCount - rowCharacters;
                if (cell.CharacterCount > remainingCharacters)
                {
                    cell = (new { value = (object?)null, cellType = "omitted", reason = "characterLimit", originalCharacterCount = cell.CharacterCount }, 0);
                    omittedByCharacterLimit = true;
                }
                rowCharacters = checked(rowCharacters + cell.CharacterCount);
                cells[offset] = cell.Value;
            }
            if (characterCount + rowCharacters > _options.Value.MaxDocumentCharacters)
            {
                stoppedByLimit = true;
                hasMoreRows = true;
                break;
            }
            rows.Add(new { rowNumber = sourceRow, cells });
            cellCount += columnLimit;
            characterCount += rowCharacters;
        }

        return ToolJson.Serialize(new
        {
            format = Format(workbookPath),
            sheetName = FileContentValidation.NormalizeText(reader.Name),
            sheetIndex,
            requestedRange = new { startRow, startColumn, maxRows = rowLimit, maxColumns = columnLimit },
            rows,
            deliveredRowCount = rows.Count,
            deliveredCellCount = cellCount,
            deliveredCharacterCount = characterCount,
            hasMoreRows,
            truncated = stoppedByLimit || omittedByCharacterLimit,
            untrustedContent = true
        });
    }

    private IExcelDataReader Open(string requestedPath)
    {
        var path = FileContentValidation.ResolveFile(_sandbox, requestedPath, _context, _options.Value);
        if (Format(path) == "unsupported") throw new FileContentToolException("UnsupportedFormat");
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration { LeaveOpen = false });
        }
        catch
        {
            stream?.Dispose();
            throw new FileContentToolException("FormatMismatch");
        }
    }

    private static (object Value, int CharacterCount) ToCell(object? value)
    {
        return value switch
        {
            null => (new { value = (object?)null, cellType = "null" }, 0),
            string text => StringCell(text),
            bool boolean => (new { value = (object)boolean, cellType = "boolean" }, 0),
            DateTime date => (new { value = (object)date.ToString("O", CultureInfo.InvariantCulture), cellType = "dateTime" }, 0),
            TimeSpan span => (new { value = (object)span.ToString("c", CultureInfo.InvariantCulture), cellType = "timeSpan" }, 0),
            double number when double.IsFinite(number) => (new { value = (object)number, cellType = "number" }, 0),
            float number when float.IsFinite(number) => (new { value = (object)number, cellType = "number" }, 0),
            decimal number => (new { value = (object)number, cellType = "number" }, 0),
            int number => (new { value = (object)number, cellType = "number" }, 0),
            long number => (new { value = (object)number, cellType = "number" }, 0),
            _ => (new { value = (object?)null, cellType = "omitted", reason = "unsupportedValue" }, 0)
        };
    }

    private static (object Value, int CharacterCount) StringCell(string text)
    {
        var normalized = FileContentValidation.NormalizeText(text);
        return (new { value = (object)normalized, cellType = "string" }, normalized.Length);
    }

    private static string Format(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".xls" => "xls",
        ".xlsx" => "xlsx",
        ".xlsb" => "xlsb",
        _ => "unsupported"
    };

    private static T Sanitize<T>(Func<T> action)
    {
        try { return action(); }
        catch (FileContentToolException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (ArgumentException) { throw; }
        catch { throw new FileContentToolException("CorruptFile"); }
    }
}
