namespace OD.MyLocalAgent.Core;

/// <summary>Configures bounded archive and local document parsing.</summary>
public sealed class FileContentOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "FileContent";
    /// <summary>Gets or sets the maximum source file size.</summary>
    public long MaxInputFileBytes { get; set; } = 100 * 1024 * 1024;
    /// <summary>Gets or sets the archive entry ceiling.</summary>
    public int MaxArchiveEntries { get; set; } = 10_000;
    /// <summary>Gets or sets the per-entry extracted byte ceiling.</summary>
    public long MaxArchiveEntryBytes { get; set; } = 250 * 1024 * 1024;
    /// <summary>Gets or sets the total extracted byte ceiling.</summary>
    public long MaxArchiveTotalBytes { get; set; } = 1024L * 1024 * 1024;
    /// <summary>Gets or sets the maximum accepted archive compression ratio.</summary>
    public int MaxArchiveCompressionRatio { get; set; } = 200;
    /// <summary>Gets or sets the list result entry ceiling.</summary>
    public int MaxListedEntries { get; set; } = 1_000;
    /// <summary>Gets or sets the extracted text character ceiling.</summary>
    public int MaxDocumentCharacters { get; set; } = 64_000;
    /// <summary>Gets or sets the spreadsheet row ceiling.</summary>
    public int MaxSpreadsheetRows { get; set; } = 1_000;
    /// <summary>Gets or sets the spreadsheet column ceiling.</summary>
    public int MaxSpreadsheetColumns { get; set; } = 256;
    /// <summary>Gets or sets the spreadsheet cell ceiling.</summary>
    public int MaxSpreadsheetCells { get; set; } = 25_000;
    /// <summary>Gets or sets the PDF page ceiling.</summary>
    public int MaxPdfPages { get; set; } = 100;

    /// <summary>Validates the configured authorization ceilings.</summary>
    public static bool IsValid(FileContentOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            return value.MaxInputFileBytes is >= 1024 * 1024 and <= 2L * 1024 * 1024 * 1024
                && value.MaxArchiveEntries is >= 1 and <= 100_000
                && value.MaxArchiveEntryBytes is >= 1024 * 1024 and <= 4L * 1024 * 1024 * 1024
                && value.MaxArchiveTotalBytes is >= 1024 * 1024 and <= 16L * 1024 * 1024 * 1024
                && value.MaxArchiveTotalBytes >= value.MaxArchiveEntryBytes
                && value.MaxArchiveCompressionRatio is >= 1 and <= 10_000
                && value.MaxListedEntries is >= 1 and <= 10_000
                && value.MaxListedEntries <= value.MaxArchiveEntries
                && value.MaxDocumentCharacters is >= 4096 and <= 1_000_000
                && value.MaxSpreadsheetRows is >= 1 and <= 100_000
                && value.MaxSpreadsheetColumns is >= 1 and <= 16_384
                && value.MaxSpreadsheetCells is >= 1 and <= 1_000_000
                && value.MaxSpreadsheetCells <= checked((long)value.MaxSpreadsheetRows * value.MaxSpreadsheetColumns)
                && value.MaxPdfPages is >= 1 and <= 10_000;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
