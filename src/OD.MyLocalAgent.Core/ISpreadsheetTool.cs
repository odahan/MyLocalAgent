namespace OD.MyLocalAgent.Core;
/// <summary>Defines bounded read-only workbook operations.</summary>
public interface ISpreadsheetTool
{
    /// <summary>Lists sheets in workbook order.</summary>
    Task<string> ListSheetsAsync(string workbookPath, CancellationToken cancellationToken = default);
    /// <summary>Reads a bounded one-based cell range.</summary>
    Task<string> ReadRangeAsync(string workbookPath, string? sheetName = null, int startRow = 1, int startColumn = 1, int? maxRows = null, int? maxColumns = null, CancellationToken cancellationToken = default);
}
