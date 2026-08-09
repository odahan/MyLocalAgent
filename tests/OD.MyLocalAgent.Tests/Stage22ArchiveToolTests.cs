using System.IO.Compression;
using System.Formats.Tar;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using SharpCompress.Common;
using SharpCompress.Writers;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies Stage 22 archive operations against isolated authorized files.</summary>
public sealed class Stage22ArchiveToolTests : IDisposable
{
    private readonly string _root;
    private readonly ArchiveTool _tool;

    /// <summary>Initializes an isolated Stage 22 archive test root.</summary>
    public Stage22ArchiveToolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Stage22", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _tool = new ArchiveTool(
            new PathSandbox(Options.Create(new FileSystemOptions { RootPath = _root })),
            Options.Create(new FileContentOptions()),
            Options.Create(new SafetyOptions { AllowArchiveWrite = true }),
            ToolExecutionContext.Taskless);
    }

    /// <summary>Verifies create, list, extract, and add preserve expected bytes.</summary>
    [Fact]
    public async Task ZipLifecycleIsAtomicAndPreservesBytes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "input"));
        Directory.CreateDirectory(Path.Combine(_root, "output"));
        await File.WriteAllTextAsync(Path.Combine(_root, "input", "first.txt"), "first");
        await File.WriteAllTextAsync(Path.Combine(_root, "input", "second.txt"), "second");

        await _tool.CreateZipAsync("archive.zip", "input/first.txt");
        var list = JsonDocument.Parse(await _tool.ListAsync("archive.zip"));
        list.RootElement.GetProperty("deliveredEntryCount").GetInt32().Should().Be(1);
        list.RootElement.GetProperty("untrustedContent").GetBoolean().Should().BeTrue();

        await _tool.AddToZipAsync("archive.zip", "input/second.txt");
        using (var zip = ZipFile.OpenRead(Path.Combine(_root, "archive.zip")))
        {
            zip.Entries.Select(static entry => entry.FullName).Should().Equal("first.txt", "second.txt");
        }

        await _tool.ExtractAsync("archive.zip", "first.txt", "output/result.txt");
        (await File.ReadAllTextAsync(Path.Combine(_root, "output", "result.txt"))).Should().Be("first");
        Directory.EnumerateFiles(_root, "*.stage", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>Verifies writes remain disabled without the independent Safety gate.</summary>
    [Fact]
    public async Task ArchiveWritesRequireSafetyOptIn()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "source.txt"), "content");
        var denied = new ArchiveTool(
            new PathSandbox(Options.Create(new FileSystemOptions { RootPath = _root })),
            Options.Create(new FileContentOptions()),
            Options.Create(new SafetyOptions()),
            ToolExecutionContext.Taskless);

        var action = () => denied.CreateZipAsync("archive.zip", "source.txt");
        await action.Should().ThrowAsync<SafetyException>();
        File.Exists(Path.Combine(_root, "archive.zip")).Should().BeFalse();
    }

    /// <summary>Verifies unsafe existing ZIP entries prevent archive replacement.</summary>
    [Fact]
    public async Task AddRejectsUnsafeExistingEntryAndPreservesArchive()
    {
        var archivePath = Path.Combine(_root, "archive.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open());
            writer.Write("unsafe");
        }

        var original = await File.ReadAllBytesAsync(archivePath);
        await File.WriteAllTextAsync(Path.Combine(_root, "source.txt"), "safe");
        var action = () => _tool.AddToZipAsync("archive.zip", "source.txt");
        var exception = await action.Should().ThrowAsync<FileContentToolException>();
        exception.Which.Category.Should().Be("UnsafeEntryPath");
        (await File.ReadAllBytesAsync(archivePath)).Should().Equal(original);
    }

    /// <summary>Verifies listing reports unsafe entries without disclosing their names.</summary>
    [Fact]
    public async Task ListReportsUnsafeEntriesWithoutReturningTheirNames()
    {
        var archivePath = Path.Combine(_root, "unsafe.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using (var safeWriter = new StreamWriter(zip.CreateEntry("safe.txt").Open()))
            {
                safeWriter.Write("safe");
            }
            using (var unsafeWriter = new StreamWriter(zip.CreateEntry("../private.txt").Open()))
            {
                unsafeWriter.Write("private");
            }
        }

        var json = await _tool.ListAsync("unsafe.zip");
        json.Should().NotContain("private.txt");
        using var result = JsonDocument.Parse(json);
        result.RootElement.GetProperty("unsafeEntryCount").GetInt32().Should().Be(1);
        result.RootElement.GetProperty("status").GetString().Should().Be("unsafeEntriesPresent");
    }

    /// <summary>Verifies directory creation uses deterministic canonical entries and store compression.</summary>
    [Fact]
    public async Task CreateZipFromDirectoryIsDeterministicAndBounded()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tree", "nested"));
        await File.WriteAllTextAsync(Path.Combine(_root, "tree", "z.txt"), "z");
        await File.WriteAllTextAsync(Path.Combine(_root, "tree", "nested", "a.txt"), "a");

        await _tool.CreateZipAsync("tree.zip", "tree", "payload", compressionMethod: "store");

        using var zip = ZipFile.OpenRead(Path.Combine(_root, "tree.zip"));
        zip.Entries.Select(entry => entry.FullName).Should().Equal(
            "payload/", "payload/nested/", "payload/nested/a.txt", "payload/z.txt");
        zip.GetEntry("payload/nested/a.txt")!.Length.Should().Be(1);
    }

    /// <summary>Verifies extraction preflight rejects case-insensitive collisions without a final directory.</summary>
    [Fact]
    public async Task ExtractAllRejectsCaseCollisionBeforeCommit()
    {
        var archivePath = Path.Combine(_root, "collision.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("same.txt");
            zip.CreateEntry("SAME.txt");
        }

        var action = () => _tool.ExtractAllAsync("collision.zip", "result");
        var exception = await action.Should().ThrowAsync<FileContentToolException>();
        exception.Which.Category.Should().Be("DuplicateEntry");
        Directory.Exists(Path.Combine(_root, "result")).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(_root, "*.stage", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>Verifies default overwrite protection preserves an existing destination.</summary>
    [Fact]
    public async Task ExtractDoesNotOverwriteByDefault()
    {
        Directory.CreateDirectory(Path.Combine(_root, "output"));
        await File.WriteAllTextAsync(Path.Combine(_root, "source.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(_root, "output", "target.txt"), "old");
        await _tool.CreateZipAsync("archive.zip", "source.txt", "source.txt");

        var action = () => _tool.ExtractAsync("archive.zip", "source.txt", "output/target.txt");
        var exception = await action.Should().ThrowAsync<FileContentToolException>();
        exception.Which.Category.Should().Be("DestinationExists");
        (await File.ReadAllTextAsync(Path.Combine(_root, "output", "target.txt"))).Should().Be("old");
    }

    /// <summary>Verifies a renamed Open XML package is not accepted as an archive input.</summary>
    [Fact]
    public async Task ListRejectsRenamedZipBasedOfficePackage()
    {
        using (var zip = ZipFile.Open(Path.Combine(_root, "renamed.zip"), ZipArchiveMode.Create))
        {
            zip.CreateEntry("[Content_Types].xml");
            zip.CreateEntry("_rels/.rels");
            zip.CreateEntry("word/document.xml");
        }

        var action = () => _tool.ListAsync("renamed.zip");
        var exception = await action.Should().ThrowAsync<FileContentToolException>();
        exception.Which.Category.Should().Be("FormatMismatch");
    }

    /// <summary>Verifies an allowlisted single-stream GZIP input exposes one safe logical entry.</summary>
    [Fact]
    public async Task ListAndExtractSingleStreamGzip()
    {
        await using (var file = File.Create(Path.Combine(_root, "payload.gz")))
        await using (var gzip = new GZipStream(file, CompressionMode.Compress))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteAsync("gzip payload");
        }
        Directory.CreateDirectory(Path.Combine(_root, "output"));

        using var listed = JsonDocument.Parse(await _tool.ListAsync("payload.gz"));
        listed.RootElement.GetProperty("deliveredEntryCount").GetInt32().Should().Be(1);
        var entryPath = listed.RootElement.GetProperty("entries")[0].GetProperty("path").GetString();
        entryPath.Should().NotBeNullOrWhiteSpace();

        await _tool.ExtractAsync("payload.gz", entryPath!, "output/payload.txt");
        (await File.ReadAllTextAsync(Path.Combine(_root, "output", "payload.txt"))).Should().Be("gzip payload");
    }

    /// <summary>Verifies a SharpCompress-backed BZIP2 stream uses the same safe logical-entry contract.</summary>
    [Fact]
    public async Task ListAndExtractSingleStreamBzip2()
    {
        await using (var file = File.Create(Path.Combine(_root, "payload.bz2")))
        await using (var bzip = SharpCompress.Compressors.BZip2.BZip2Stream.Create(
            file, SharpCompress.Compressors.CompressionMode.Compress, decompressConcatenated: false, leaveOpen: false, tolerateTruncatedStream: false))
        await using (var writer = new StreamWriter(bzip))
        {
            await writer.WriteAsync("bzip2 payload");
        }
        Directory.CreateDirectory(Path.Combine(_root, "output"));

        using var listed = JsonDocument.Parse(await _tool.ListAsync("payload.bz2"));
        var entryPath = listed.RootElement.GetProperty("entries")[0].GetProperty("path").GetString();
        entryPath.Should().Be("payload");
        await _tool.ExtractAsync("payload.bz2", entryPath!, "output/payload.txt");
        (await File.ReadAllTextAsync(Path.Combine(_root, "output", "payload.txt"))).Should().Be("bzip2 payload");
    }

    /// <summary>Verifies plain and GZIP-compressed TAR containers are detected and extracted.</summary>
    [Theory]
    [InlineData("sample.tar", false)]
    [InlineData("sample.tar.gz", true)]
    public async Task ListAndExtractTarContainers(string archiveName, bool compressed)
    {
        var source = Path.Combine(_root, "tar-source.txt");
        await File.WriteAllTextAsync(source, "tar payload");
        await using (var file = File.Create(Path.Combine(_root, archiveName)))
        {
            Stream target = file;
            if (compressed) target = new GZipStream(file, CompressionMode.Compress, leaveOpen: true);
            using (target)
            using (var writer = new TarWriter(target, leaveOpen: true))
            {
                writer.WriteEntry(source, "folder/payload.txt");
            }
        }
        Directory.CreateDirectory(Path.Combine(_root, "tar-output"));
        await using (var verificationFile = File.OpenRead(Path.Combine(_root, archiveName)))
        {
            Stream verificationStream = verificationFile;
            if (compressed) verificationStream = new GZipStream(verificationFile, CompressionMode.Decompress, leaveOpen: true);
            using (verificationStream)
            using (var reader = new TarReader(verificationStream, leaveOpen: true))
            {
                reader.GetNextEntry().Should().NotBeNull();
            }
        }

        using var listed = JsonDocument.Parse(await _tool.ListAsync(archiveName));
        listed.RootElement.GetProperty("entries")[0].GetProperty("path").GetString().Should().Be("folder/payload.txt");
        await _tool.ExtractAsync(archiveName, "folder/payload.txt", $"tar-output/{(compressed ? "compressed" : "plain")}.txt");
        (await File.ReadAllTextAsync(Path.Combine(_root, "tar-output", compressed ? "compressed.txt" : "plain.txt"))).Should().Be("tar payload");
    }

    /// <summary>Verifies the allowlisted 7z container is readable while remaining unavailable for product writes.</summary>
    [Fact]
    public async Task ListAndExtractSevenZip()
    {
        await using (var file = File.Create(Path.Combine(_root, "sample.7z")))
        using (var writer = WriterFactory.OpenWriter(file, ArchiveType.SevenZip, new WriterOptions(CompressionType.LZMA) { LeaveStreamOpen = true }))
        await using (var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("7z payload")))
        {
            writer.Write("folder/payload.txt", input, DateTime.UtcNow);
        }
        Directory.CreateDirectory(Path.Combine(_root, "seven-output"));

        using var listed = JsonDocument.Parse(await _tool.ListAsync("sample.7z"));
        listed.RootElement.GetProperty("entries")[0].GetProperty("path").GetString().Should().Be("folder/payload.txt");
        await _tool.ExtractAsync("sample.7z", "folder/payload.txt", "seven-output/payload.txt");
        (await File.ReadAllTextAsync(Path.Combine(_root, "seven-output", "payload.txt"))).Should().Be("7z payload");
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
