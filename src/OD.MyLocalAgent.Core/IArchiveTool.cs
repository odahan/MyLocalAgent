namespace OD.MyLocalAgent.Core;

/// <summary>Defines bounded archive inspection and ZIP-only mutation operations.</summary>
public interface IArchiveTool
{
    /// <summary>Lists supported archive entries.</summary>
    Task<string> ListAsync(string archivePath, int? maxEntries = null, CancellationToken cancellationToken = default);
    /// <summary>Extracts one regular archive entry.</summary>
    Task<string> ExtractAsync(string archivePath, string entryPath, string destinationPath, bool overwrite = false, CancellationToken cancellationToken = default);
    /// <summary>Extracts all regular archive entries into a new directory.</summary>
    Task<string> ExtractAllAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default);
    /// <summary>Creates a ZIP archive from an authorized source.</summary>
    Task<string> CreateZipAsync(string archivePath, string sourcePath, string? entryPath = null, bool overwriteArchive = false, string? compressionMethod = null, string? compressionLevel = null, CancellationToken cancellationToken = default);
    /// <summary>Adds an authorized source to a ZIP archive.</summary>
    Task<string> AddToZipAsync(string archivePath, string sourcePath, string? entryPath = null, bool overwriteEntry = false, string? compressionMethod = null, string? compressionLevel = null, CancellationToken cancellationToken = default);
}
