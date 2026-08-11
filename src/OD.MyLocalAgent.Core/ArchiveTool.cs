using System.IO.Compression;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OD.MyLocalAgent.Core;

/// <summary>Implements bounded archive reads and staged ZIP-only writes.</summary>
[ToolGroup("archive", "Archives", "Inspect common authorized archives and extract or create ZIP files with explicit safety approval.", 600, Activation = ToolActivationKind.Contextual, ServiceContract = typeof(IArchiveTool))]
public sealed class ArchiveTool : IArchiveTool
{
    private static readonly string[] AllowedExtensions =
    [
        ".tar.bz2", ".tar.zst", ".tar.gz", ".tar.lz", ".tar.xz", ".tbz2", ".tzst", ".tgz", ".txz",
        ".zip", ".7z", ".rar", ".tar", ".bz2", ".zst", ".gz", ".lz", ".xz"
    ];

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly IPathSandbox _sandbox;
    private readonly IOptions<FileContentOptions> _options;
    private readonly ToolExecutionContext _context;

    /// <summary>Initializes a new instance of the <see cref="ArchiveTool"/> class.</summary>
    [ActivatorUtilitiesConstructor]
    public ArchiveTool(IPathSandbox sandbox, IOptions<FileContentOptions> options, ToolExecutionContext context)
    {
        _sandbox = sandbox;
        _options = options;
        _context = context;
    }

    /// <inheritdoc/>
    [ToolLocalPath("archivePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [Tool("archive.list", "Lists bounded entries in an authorized supported archive.", 100, AgentFunctionName = "archive_list")]
    public Task<string> ListAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized archive path.", Example = "archives/input.zip")] string archivePath,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional maximum listed entries.", Example = "100")] int? maxEntries = null,
        CancellationToken cancellationToken = default) => Task.Run(() => List(archivePath, maxEntries, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    [ToolLocalPath("archivePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [ToolLocalPath("destinationPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]
    [Tool("archive.extract", "Extracts one safe archive file entry to an authorized destination.", 200, AgentFunctionName = "archive_extract", RequiredCapability = TaskCapability.ArchiveWrite)]
    public async Task<string> ExtractAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized archive path.", Example = "archives/input.zip")] string archivePath,
        [ToolArgument(WorkflowArgumentKind.String, "Canonical archive entry path.", Example = "notes/readme.txt")] string entryPath,
        [ToolArgument(WorkflowArgumentKind.String, "Authorized destination file path.", Example = "output/readme.txt")] string destinationPath,
        [ToolArgument(WorkflowArgumentKind.Boolean, "Whether an existing destination may be replaced.", Example = "false")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        EnsureWrite();
        var requestedEntry = ValidateEntryPath(entryPath);
        using var opened = OpenArchive(archivePath);
        var entries = ValidateArchiveEntries(opened.Archive, rejectUnsafe: true, opened.SingleStreamEntryName);
        var descriptor = entries.FirstOrDefault(item => string.Equals(item.Path, requestedEntry, StringComparison.Ordinal))
            ?? throw new FileContentToolException("EntryNotFound");
        if (descriptor.IsDirectory || descriptor.Entry.IsEncrypted || !string.IsNullOrEmpty(descriptor.Entry.LinkTarget))
            throw new FileContentToolException(descriptor.Entry.IsEncrypted ? "EncryptedFile" : "UnsupportedEntryType");

        var destination = _sandbox.Resolve(destinationPath, FileSystemAccessMode.ReadWrite, _context);
        EnsureExistingParent(destination);
        if (File.Exists(destination) && !overwrite) throw new FileContentToolException("DestinationExists");
        var temporary = TemporarySibling(destination);
        try
        {
            await using (var input = descriptor.Entry.OpenEntryStream())
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.WriteThrough))
            {
                await CopyBoundedAsync(input, output, descriptor.Entry.CompressedSize, _options.Value.MaxArchiveTotalBytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            Revalidate(destinationPath, destination, FileSystemAccessMode.ReadWrite);
            CommitFile(temporary, destination, overwrite);
            return ToolJson.Serialize(new { operation = "extract", format = opened.Format, success = true, overwrite, untrustedContent = true });
        }
        finally { DeleteOwnedFile(temporary); }
    }

    /// <inheritdoc/>
    [ToolLocalPath("archivePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]
    [ToolLocalPath("destinationDirectory", LocalPathTargetKind.Directory, FileSystemAccessMode.ReadWrite)]
    [Tool("archive.extractAll", "Extracts safe archive entries to a new authorized directory.", 300, AgentFunctionName = "archive_extract_all", RequiredCapability = TaskCapability.ArchiveWrite)]
    public async Task<string> ExtractAllAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized archive path.", Example = "archives/input.zip")] string archivePath,
        [ToolArgument(WorkflowArgumentKind.String, "New authorized destination directory.", Example = "output/extracted")] string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        EnsureWrite();
        var destination = _sandbox.Resolve(destinationDirectory, FileSystemAccessMode.ReadWrite, _context);
        EnsureExistingParent(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new FileContentToolException("DestinationExists");
        using var opened = OpenArchive(archivePath);
        var entries = ValidateArchiveEntries(opened.Archive, rejectUnsafe: true, opened.SingleStreamEntryName);
        var stage = TemporarySibling(destination);
        Directory.CreateDirectory(stage);
        long totalBytes = 0;
        var extractedEntries = 0;
        try
        {
            foreach (var descriptor in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = CombineUnderRoot(stage, descriptor.Path);
                if (descriptor.IsDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                if (descriptor.Entry.IsEncrypted) throw new FileContentToolException("EncryptedFile");
                if (!string.IsNullOrEmpty(descriptor.Entry.LinkTarget)) throw new FileContentToolException("UnsupportedEntryType");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = descriptor.Entry.OpenEntryStream();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.WriteThrough);
                var remaining = _options.Value.MaxArchiveTotalBytes - totalBytes;
                totalBytes = checked(totalBytes + await CopyBoundedAsync(input, output, descriptor.Entry.CompressedSize, remaining, cancellationToken).ConfigureAwait(false));
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                extractedEntries++;
            }
            Revalidate(destinationDirectory, destination, FileSystemAccessMode.ReadWrite);
            Directory.Move(stage, destination);
            return ToolJson.Serialize(new { operation = "extractAll", format = opened.Format, entryCount = extractedEntries, extractedBytes = totalBytes, success = true, untrustedContent = true });
        }
        finally { DeleteOwnedDirectory(stage); }
    }

    /// <inheritdoc/>
    [ToolLocalPath("archivePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]
    [ToolLocalPath("sourcePath", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadOnly)]
    [Tool("archive.createZip", "Creates a ZIP from an authorized file or directory source.", 400, AgentFunctionName = "archive_create_zip", RequiredCapability = TaskCapability.ArchiveWrite)]
    public Task<string> CreateZipAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized ZIP destination path.", Example = "archives/output.zip")] string archivePath,
        [ToolArgument(WorkflowArgumentKind.String, "Authorized source file or directory path.", Example = "input/readme.txt")] string sourcePath,
        [ToolArgument(WorkflowArgumentKind.String, "Optional canonical ZIP entry path.", Example = "readme.txt")] string? entryPath = null,
        [ToolArgument(WorkflowArgumentKind.Boolean, "Whether an existing ZIP may be replaced.", Example = "false")] bool overwriteArchive = false,
        [ToolArgument(WorkflowArgumentKind.String, "Optional compression method.", Example = "deflate", AllowedValues = ["deflate", "store"])] string? compressionMethod = null,
        [ToolArgument(WorkflowArgumentKind.String, "Optional Deflate compression level.", Example = "balanced", AllowedValues = ["fast", "balanced", "smallest"])] string? compressionLevel = null,
        CancellationToken cancellationToken = default) => CreateAsync(archivePath, sourcePath, entryPath, overwriteArchive, compressionMethod, compressionLevel, cancellationToken);

    /// <inheritdoc/>
    [ToolLocalPath("archivePath", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]
    [ToolLocalPath("sourcePath", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadOnly)]
    [Tool("archive.addToZip", "Adds an authorized file or directory source to a ZIP without in-place mutation.", 500, AgentFunctionName = "archive_add_to_zip", RequiredCapability = TaskCapability.ArchiveWrite)]
    public Task<string> AddToZipAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized ZIP path.", Example = "archives/output.zip")] string archivePath,
        [ToolArgument(WorkflowArgumentKind.String, "Authorized source file or directory path.", Example = "input/readme.txt")] string sourcePath,
        [ToolArgument(WorkflowArgumentKind.String, "Optional canonical ZIP entry path.", Example = "readme.txt")] string? entryPath = null,
        [ToolArgument(WorkflowArgumentKind.Boolean, "Whether existing regular entries may be replaced.", Example = "false")] bool overwriteEntry = false,
        [ToolArgument(WorkflowArgumentKind.String, "Optional compression method.", Example = "deflate", AllowedValues = ["deflate", "store"])] string? compressionMethod = null,
        [ToolArgument(WorkflowArgumentKind.String, "Optional Deflate compression level.", Example = "balanced", AllowedValues = ["fast", "balanced", "smallest"])] string? compressionLevel = null,
        CancellationToken cancellationToken = default) => AddAsync(archivePath, sourcePath, entryPath, overwriteEntry, compressionMethod, compressionLevel, cancellationToken);

    private string List(string archivePath, int? maxEntries, CancellationToken cancellationToken)
    {
        var limit = FileContentValidation.Limit(maxEntries, _options.Value.MaxListedEntries);
        using var opened = OpenArchive(archivePath);
        var delivered = new List<object>();
        var total = 0;
        var unsafeCount = 0;
        var duplicateCount = 0;
        var encryptedCount = 0;
        var specialCount = 0;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in opened.Archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total++;
            string? safe = null;
            try { safe = ValidateEntryPath(EntryPath(entry, opened.SingleStreamEntryName)); }
            catch (FileContentToolException) { unsafeCount++; }
            if (safe is not null && !known.Add(safe)) duplicateCount++;
            if (entry.IsEncrypted) encryptedCount++;
            if (!string.IsNullOrEmpty(entry.LinkTarget)) specialCount++;
            if (safe is not null && delivered.Count < limit)
            {
                delivered.Add(new
                {
                    path = safe,
                    entryType = entry.IsDirectory ? "directory" : "file",
                    compressedBytes = entry.CompressedSize < 0 ? (long?)null : entry.CompressedSize,
                    uncompressedBytes = entry.Size < 0 ? (long?)null : entry.Size,
                    modifiedUtc = entry.LastModifiedTime?.ToUniversalTime(),
                    encrypted = entry.IsEncrypted
                });
            }
        }
        var truncated = total > delivered.Count;
        return ToolJson.Serialize(new
        {
            format = opened.Format,
            totalEntryCount = total,
            deliveredEntryCount = delivered.Count,
            entries = delivered,
            unsafeEntryCount = unsafeCount,
            duplicateEntryCount = duplicateCount,
            encryptedEntryCount = encryptedCount,
            unsupportedEntryCount = specialCount,
            status = unsafeCount + duplicateCount + encryptedCount + specialCount == 0 ? "safe" : "unsafeEntriesPresent",
            hasMoreEntries = truncated,
            truncated,
            untrustedContent = true
        });
    }

    private async Task<string> CreateAsync(string archivePath, string sourcePath, string? entryPath, bool overwrite, string? method, string? level, CancellationToken cancellationToken)
    {
        EnsureWrite();
        var compression = ResolveCompression(method, level);
        var destination = _sandbox.Resolve(archivePath, FileSystemAccessMode.ReadWrite, _context);
        EnsureZipExtension(destination);
        EnsureExistingParent(destination);
        if (File.Exists(destination) && !overwrite) throw new FileContentToolException("DestinationExists");
        var source = _sandbox.Resolve(sourcePath, FileSystemAccessMode.ReadOnly, _context);
        var sourceEntries = CollectSourceEntries(source, entryPath, cancellationToken);
        var temporary = TemporarySibling(destination);
        try
        {
            await WriteZipAsync(temporary, [], sourceEntries, compression.Level, cancellationToken).ConfigureAwait(false);
            ValidateReplacementZip(temporary);
            Revalidate(archivePath, destination, FileSystemAccessMode.ReadWrite);
            Revalidate(sourcePath, source, FileSystemAccessMode.ReadOnly);
            CommitFile(temporary, destination, overwrite);
            return ToolJson.Serialize(new { operation = "createZip", format = "zip", entryCount = sourceEntries.Count, overwrite, compressionMethod = compression.Method, compressionLevel = compression.Name, success = true, untrustedContent = true });
        }
        finally { DeleteOwnedFile(temporary); }
    }

    private async Task<string> AddAsync(string archivePath, string sourcePath, string? entryPath, bool overwrite, string? method, string? level, CancellationToken cancellationToken)
    {
        EnsureWrite();
        var compression = ResolveCompression(method, level);
        var destination = _sandbox.Resolve(archivePath, FileSystemAccessMode.ReadWrite, _context);
        EnsureZipExtension(destination);
        if (!File.Exists(destination)) throw new FileContentToolException("CorruptFile");
        using (var opened = OpenArchive(archivePath))
        {
            if (opened.Format != "zip") throw new FileContentToolException("FormatMismatch");
            _ = ValidateArchiveEntries(opened.Archive, rejectUnsafe: true, opened.SingleStreamEntryName);
        }
        var source = _sandbox.Resolve(sourcePath, FileSystemAccessMode.ReadOnly, _context);
        var sourceEntries = CollectSourceEntries(source, entryPath, cancellationToken);
        var temporary = TemporarySibling(destination);
        try
        {
            using var existing = ZipFile.OpenRead(destination);
            var oldEntries = existing.Entries.Select(ToExistingEntry).ToArray();
            ValidateMergedGraph(oldEntries, sourceEntries, overwrite);
            await WriteZipAsync(temporary, oldEntries, sourceEntries, compression.Level, cancellationToken, overwrite).ConfigureAwait(false);
            existing.Dispose();
            ValidateReplacementZip(temporary);
            Revalidate(archivePath, destination, FileSystemAccessMode.ReadWrite);
            Revalidate(sourcePath, source, FileSystemAccessMode.ReadOnly);
            CommitFile(temporary, destination, overwrite: true);
            return ToolJson.Serialize(new { operation = "addToZip", format = "zip", entryCount = sourceEntries.Count, overwrite, compressionMethod = compression.Method, compressionLevel = compression.Name, success = true, untrustedContent = true });
        }
        finally { DeleteOwnedFile(temporary); }
    }

    private async Task WriteZipAsync(string target, IReadOnlyList<ExistingEntry> existingEntries, IReadOnlyList<SourceEntry> sourceEntries, CompressionLevel compression, CancellationToken cancellationToken, bool overwrite = false)
    {
        var replacementNames = sourceEntries.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81_920, FileOptions.WriteThrough);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var old in existingEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (overwrite && replacementNames.Contains(old.Path)) continue;
            var entry = zip.CreateEntry(old.IsDirectory ? old.Path + "/" : old.Path, CompressionLevel.Optimal);
            entry.LastWriteTime = old.LastWriteTime;
            if (!old.IsDirectory)
            {
                await using var input = old.Entry.Open();
                await using var output = entry.Open();
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
        foreach (var source in sourceEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = zip.CreateEntry(source.IsDirectory ? source.Path + "/" : source.Path, source.IsDirectory ? CompressionLevel.NoCompression : compression);
            entry.LastWriteTime = source.LastWriteTime;
            if (!source.IsDirectory)
            {
                await using var input = new FileStream(source.SourcePath!, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var output = entry.Open();
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private List<SourceEntry> CollectSourceEntries(string source, string? entryPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileContentToolException("CorruptFile");
        RejectReparsePoint(source);
        var root = ValidateEntryPath(entryPath ?? Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        var entries = new List<SourceEntry>();
        long totalBytes = 0;
        if (File.Exists(source)) AddFile(source, root);
        else
        {
            entries.Add(new SourceEntry(root, null, true, NormalizeZipTime(Directory.GetLastWriteTimeUtc(source))));
            Walk(source, root);
        }
        if (entries.Count > _options.Value.MaxArchiveEntries) throw new FileContentToolException("LimitExceeded");
        ValidatePathGraph(entries.Select(item => (item.Path, item.IsDirectory)));
        return entries.OrderBy(item => item.Path, StringComparer.Ordinal).ToList();

        void Walk(string directory, string archiveRoot)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectReparsePoint(item);
                var relative = Path.GetRelativePath(source, item).Replace('\\', '/');
                var archiveName = ValidateEntryPath($"{archiveRoot}/{relative}");
                if (Directory.Exists(item))
                {
                    entries.Add(new SourceEntry(archiveName, null, true, NormalizeZipTime(Directory.GetLastWriteTimeUtc(item))));
                    Walk(item, archiveRoot);
                }
                else if (File.Exists(item)) AddFile(item, archiveName);
                else throw new FileContentToolException("UnsupportedEntryType");
                if (entries.Count > _options.Value.MaxArchiveEntries) throw new FileContentToolException("LimitExceeded");
            }
        }

        void AddFile(string file, string archiveName)
        {
            var info = new FileInfo(file);
            if (info.Length > _options.Value.MaxArchiveEntryBytes) throw new FileContentToolException("LimitExceeded");
            totalBytes = checked(totalBytes + info.Length);
            if (totalBytes > _options.Value.MaxArchiveTotalBytes) throw new FileContentToolException("LimitExceeded");
            entries.Add(new SourceEntry(archiveName, file, false, NormalizeZipTime(info.LastWriteTimeUtc)));
        }
    }

    private List<ArchiveEntryDescriptor> ValidateArchiveEntries(IArchive archive, bool rejectUnsafe, string? singleStreamEntryName = null)
    {
        var result = new List<ArchiveEntryDescriptor>();
        long totalDeclaredBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (result.Count >= _options.Value.MaxArchiveEntries) throw new FileContentToolException("LimitExceeded");
            try
            {
                var safe = ValidateEntryPath(EntryPath(entry, singleStreamEntryName));
                if (entry.IsEncrypted) throw new FileContentToolException("EncryptedFile");
                if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new FileContentToolException("UnsupportedEntryType");
                if (!entry.IsDirectory && entry.Size >= 0)
                {
                    if (entry.Size > _options.Value.MaxArchiveEntryBytes) throw new FileContentToolException("LimitExceeded");
                    try { totalDeclaredBytes = checked(totalDeclaredBytes + entry.Size); }
                    catch (OverflowException) { throw new FileContentToolException("LimitExceeded"); }
                    if (totalDeclaredBytes > _options.Value.MaxArchiveTotalBytes
                        || entry.CompressedSize > 0 && (decimal)entry.Size / entry.CompressedSize > _options.Value.MaxArchiveCompressionRatio)
                        throw new FileContentToolException("LimitExceeded");
                }
                result.Add(new ArchiveEntryDescriptor(safe, entry.IsDirectory, entry));
            }
            catch (FileContentToolException) when (!rejectUnsafe) { }
        }
        ValidatePathGraph(result.Select(item => (item.Path, item.IsDirectory)));
        return result;
    }

    private static void ValidatePathGraph(IEnumerable<(string Path, bool IsDirectory)> entries)
    {
        var kinds = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, isDirectory) in entries)
        {
            if (!kinds.TryAdd(path, isDirectory)) throw new FileContentToolException("DuplicateEntry");
            var segments = path.Split('/');
            var prefix = segments[0];
            for (var index = 1; index < segments.Length; index++)
            {
                if (kinds.TryGetValue(prefix, out var prefixIsDirectory) && !prefixIsDirectory)
                    throw new FileContentToolException("DuplicateEntry");
                prefix += "/" + segments[index];
            }
            if (!isDirectory && kinds.Keys.Any(candidate => candidate.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)))
                throw new FileContentToolException("DuplicateEntry");
        }
    }

    private static void ValidateMergedGraph(IReadOnlyList<ExistingEntry> oldEntries, IReadOnlyList<SourceEntry> sourceEntries, bool overwrite)
    {
        var old = oldEntries.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var source in sourceEntries)
        {
            if (!old.TryGetValue(source.Path, out var collision)) continue;
            if (source.IsDirectory != collision.IsDirectory || !overwrite && !source.IsDirectory)
                throw new FileContentToolException("DuplicateEntry");
        }
        var merged = oldEntries.Where(item => !overwrite || !sourceEntries.Any(source => !source.IsDirectory && string.Equals(source.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
            .Select(item => (item.Path, item.IsDirectory))
            .Concat(sourceEntries.Where(source => !source.IsDirectory || !old.ContainsKey(source.Path)).Select(source => (source.Path, source.IsDirectory)));
        ValidatePathGraph(merged);
    }

    private OpenedArchive OpenArchive(string requestedPath)
    {
        var path = FileContentValidation.ResolveFile(_sandbox, requestedPath, _context, _options.Value);
        var format = FormatFromPath(path);
        if (format is null) throw new FileContentToolException("UnsupportedFormat");
        string? ownedTemporaryPath = null;
        try
        {
            var archive = OpenDetectedArchive(path, format, out ownedTemporaryPath);
            var syntheticSingleStream = format is "bz2" or "lz" or "xz" or "zst";
            if (!MatchesDetectedType(format, archive.Type) && !(syntheticSingleStream && archive.Type == ArchiveType.Zip))
            {
                archive.Dispose();
                throw new FileContentToolException("FormatMismatch");
            }
            if (format == "zip" && IsKnownZipBasedContainer(archive))
            {
                archive.Dispose();
                throw new FileContentToolException("FormatMismatch");
            }
            if (archive.Entries.Take(_options.Value.MaxArchiveEntries + 1).Count() > _options.Value.MaxArchiveEntries)
            {
                archive.Dispose();
                throw new FileContentToolException("LimitExceeded");
            }
            var singleStreamName = format is "gz" or "bz2" or "lz" or "xz" or "zst"
                ? ValidateEntryPath(Path.GetFileNameWithoutExtension(path))
                : null;
            return new OpenedArchive(archive, format, singleStreamName, ownedTemporaryPath);
        }
        catch (FileContentToolException)
        {
            if (ownedTemporaryPath is not null) DeleteOwnedFile(ownedTemporaryPath);
            throw;
        }
        catch
        {
            if (ownedTemporaryPath is not null) DeleteOwnedFile(ownedTemporaryPath);
            throw new FileContentToolException("CorruptFile");
        }
    }

    private void ValidateReplacementZip(string path)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(path, new ReaderOptions { LookForHeader = true });
            if (archive.Type != ArchiveType.Zip) throw new FileContentToolException("FormatMismatch");
            _ = ValidateArchiveEntries(archive, rejectUnsafe: true);
        }
        catch (FileContentToolException) { throw; }
        catch { throw new FileContentToolException("CorruptFile"); }
    }

    private IArchive OpenDetectedArchive(string path, string format, out string? ownedTemporaryPath)
    {
        ownedTemporaryPath = null;
        if (format is "bz2" or "lz" or "xz" or "zst")
            return OpenSyntheticSingleStreamArchive(path, format, out ownedTemporaryPath);
        if (format is not ("tar.gz" or "tar.bz2" or "tar.lz" or "tar.xz" or "tar.zst"))
            return ArchiveFactory.OpenArchive(path, new ReaderOptions { LookForHeader = true });

        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var compressedLength = file.Length;
        Stream? decompressor = null;
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"OD.MyLocalAgent.{Guid.NewGuid():N}.tar");
        try
        {
            decompressor = CreateDecompressor(file, format[4..]);
            using (decompressor)
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.WriteThrough))
            {
                var buffer = new byte[81_920];
                long total = 0;
                int read;
                while ((read = decompressor.Read(buffer, 0, buffer.Length)) != 0)
                {
                    total = checked(total + read);
                    if (total > _options.Value.MaxArchiveTotalBytes || compressedLength > 0 && (decimal)total / compressedLength > _options.Value.MaxArchiveCompressionRatio)
                        throw new FileContentToolException("LimitExceeded");
                    output.Write(buffer, 0, read);
                }
                output.Flush(flushToDisk: true);
            }
            var archive = ArchiveFactory.OpenArchive(temporaryPath, new ReaderOptions { LookForHeader = true });
            ownedTemporaryPath = temporaryPath;
            return archive;
        }
        catch
        {
            decompressor?.Dispose();
            if (decompressor is null) file.Dispose();
            DeleteOwnedFile(temporaryPath);
            throw;
        }
    }

    private IArchive OpenSyntheticSingleStreamArchive(string path, string format, out string? ownedTemporaryPath)
    {
        ownedTemporaryPath = null;
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"OD.MyLocalAgent.{Guid.NewGuid():N}.zip");
        var logicalName = ValidateEntryPath(Path.GetFileNameWithoutExtension(path));
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var compressedLength = file.Length;
            using var decompressor = CreateDecompressor(file, format);
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81_920, FileOptions.WriteThrough))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            using (var entryOutput = zip.CreateEntry(logicalName, CompressionLevel.NoCompression).Open())
            {
                var buffer = new byte[81_920];
                long total = 0;
                int read;
                while ((read = decompressor.Read(buffer, 0, buffer.Length)) != 0)
                {
                    total = checked(total + read);
                    if (total > _options.Value.MaxArchiveEntryBytes
                        || total > _options.Value.MaxArchiveTotalBytes
                        || compressedLength > 0 && (decimal)total / compressedLength > _options.Value.MaxArchiveCompressionRatio)
                        throw new FileContentToolException("LimitExceeded");
                    entryOutput.Write(buffer, 0, read);
                }
            }
            var archive = ArchiveFactory.OpenArchive(temporaryPath, new ReaderOptions { LookForHeader = true });
            ownedTemporaryPath = temporaryPath;
            return archive;
        }
        catch
        {
            DeleteOwnedFile(temporaryPath);
            throw;
        }
    }

    private static Stream CreateDecompressor(Stream file, string format) => format switch
    {
        "gz" => new GZipStream(file, CompressionMode.Decompress, leaveOpen: false),
        "bz2" => SharpCompress.Compressors.BZip2.BZip2Stream.Create(
            file, SharpCompress.Compressors.CompressionMode.Decompress, decompressConcatenated: true, leaveOpen: false, tolerateTruncatedStream: false),
        "lz" => SharpCompress.Compressors.LZMA.LZipStream.Create(
            file, SharpCompress.Compressors.CompressionMode.Decompress, leaveOpen: false),
        "xz" => new SharpCompress.Compressors.Xz.XZStream(file),
        "zst" => new SharpCompress.Compressors.ZStandard.DecompressionStream(
            file, 81_920, checkEndOfStream: true, leaveOpen: false),
        _ => throw new FileContentToolException("UnsupportedFormat")
    };

    private async Task<long> CopyBoundedAsync(Stream input, Stream output, long compressedBytes, long remainingTotal, CancellationToken cancellationToken)
    {
        if (remainingTotal < 0) throw new FileContentToolException("LimitExceeded");
        var buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total = checked(total + read);
            if (total > _options.Value.MaxArchiveEntryBytes || total > remainingTotal || compressedBytes > 0 && (decimal)total / compressedBytes > _options.Value.MaxArchiveCompressionRatio)
                throw new FileContentToolException("LimitExceeded");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return total;
    }

    private void EnsureWrite()
    {
    }

    private void Revalidate(string requestedPath, string expectedPath, FileSystemAccessMode access)
    {
        var resolved = _sandbox.Resolve(requestedPath, access, _context);
        if (!string.Equals(resolved, expectedPath, StringComparison.OrdinalIgnoreCase)) throw new FileContentToolException("UnsafeEntryPath");
    }

    private static string ValidateEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Any(char.IsControl) || Path.IsPathRooted(path) || path.StartsWith("//", StringComparison.Ordinal))
            throw new FileContentToolException("UnsafeEntryPath");
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.Contains(':') || segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new FileContentToolException("UnsafeEntryPath");
            if (ReservedDeviceNames.Contains(segment.Split('.')[0])) throw new FileContentToolException("UnsafeEntryPath");
        }
        return path;
    }

    private static string CombineUnderRoot(string root, string entryPath)
    {
        var combined = Path.GetFullPath(Path.Combine(root, entryPath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new FileContentToolException("UnsafeEntryPath");
        return combined;
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new FileContentToolException("UnsupportedEntryType");
    }

    private static void EnsureZipExtension(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new FileContentToolException("UnsupportedFormat");
    }

    private static void EnsureExistingParent(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent)) throw new FileContentToolException("DestinationExists");
        RejectReparsePoint(parent);
    }

    private static (string Method, string? Name, CompressionLevel Level) ResolveCompression(string? method, string? level)
    {
        method ??= "deflate";
        if (method is not ("deflate" or "store")) throw new ArgumentOutOfRangeException(nameof(method));
        if (method == "store")
        {
            if (level is not null) throw new ArgumentException("Compression level must be omitted for store.", nameof(level));
            return (method, null, CompressionLevel.NoCompression);
        }
        level ??= "balanced";
        return level switch
        {
            "fast" => (method, level, CompressionLevel.Fastest),
            "balanced" => (method, level, CompressionLevel.Optimal),
            "smallest" => (method, level, CompressionLevel.SmallestSize),
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };
    }

    private static ExistingEntry ToExistingEntry(ZipArchiveEntry entry)
    {
        var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
        return new ExistingEntry(ValidateEntryPath(entry.FullName.TrimEnd('/')), entry, isDirectory, NormalizeZipTime(entry.LastWriteTime.UtcDateTime));
    }

    private static DateTimeOffset NormalizeZipTime(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        if (utc.Year < 1980) utc = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (utc.Year > 2107) utc = new DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Utc);
        return new DateTimeOffset(utc.AddTicks(-(utc.Ticks % (TimeSpan.TicksPerSecond * 2))));
    }

    private static string? FormatFromPath(string path)
    {
        var extension = AllowedExtensions.FirstOrDefault(item => path.EndsWith(item, StringComparison.OrdinalIgnoreCase));
        return extension?.TrimStart('.') switch
        {
            "tgz" => "tar.gz",
            "tbz2" => "tar.bz2",
            "txz" => "tar.xz",
            "tzst" => "tar.zst",
            var value => value
        };
    }

    private static bool MatchesDetectedType(string format, ArchiveType type) => format switch
    {
        "zip" => type == ArchiveType.Zip,
        "7z" => type == ArchiveType.SevenZip,
        "rar" => type == ArchiveType.Rar,
        "tar" or "tar.gz" or "tar.bz2" or "tar.lz" or "tar.xz" or "tar.zst" => type == ArchiveType.Tar,
        "gz" or "bz2" or "lz" or "xz" or "zst" => type == ArchiveType.GZip,
        _ => false
    };

    private static bool IsKnownZipBasedContainer(IArchive archive)
    {
        var names = archive.Entries.Select(entry => entry.Key ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Contains("[Content_Types].xml")
            || names.Contains("META-INF/MANIFEST.MF")
            || names.Contains("mimetype") && names.Any(name => name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase));
    }

    private static string EntryPath(IArchiveEntry entry, string? singleStreamEntryName)
    {
        var path = (entry.Key ?? string.Empty).TrimEnd('/');
        return string.IsNullOrWhiteSpace(path) && singleStreamEntryName is not null ? singleStreamEntryName : path;
    }

    private static string TemporarySibling(string target) => Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.stage");

    private static void CommitFile(string temporary, string destination, bool overwrite)
    {
        if (File.Exists(destination))
        {
            if (!overwrite) throw new FileContentToolException("DestinationExists");
            File.Move(temporary, destination, overwrite: true);
        }
        else File.Move(temporary, destination);
    }

    private static void DeleteOwnedFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteOwnedDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ArchiveEntryDescriptor(string Path, bool IsDirectory, IArchiveEntry Entry);
    private sealed record SourceEntry(string Path, string? SourcePath, bool IsDirectory, DateTimeOffset LastWriteTime);
    private sealed record ExistingEntry(string Path, ZipArchiveEntry Entry, bool IsDirectory, DateTimeOffset LastWriteTime);

    private sealed class OpenedArchive(IArchive archive, string format, string? singleStreamEntryName, string? ownedTemporaryPath) : IDisposable
    {
        public IArchive Archive { get; } = archive;
        public string Format { get; } = format;
        public string? SingleStreamEntryName { get; } = singleStreamEntryName;
        public void Dispose()
        {
            Archive.Dispose();
            if (ownedTemporaryPath is not null) DeleteOwnedFile(ownedTemporaryPath);
        }
    }
}
