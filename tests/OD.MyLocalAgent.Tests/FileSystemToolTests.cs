using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies sandboxed file-system operations and destructive safety gating.
/// </summary>
public sealed class FileSystemToolTests : IDisposable
{
    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _authorizedPath =
        Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Authorized", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies the workspace listing does not reveal application-owned data.
    /// </summary>
    [Fact]
    public async Task WorkspaceListingOmitsApplicationOwnedDirectory()
    {
        Directory.CreateDirectory(Path.Combine(
            _rootPath,
            WorkspacePaths.ApplicationDirectoryName));
        Directory.CreateDirectory(Path.Combine(_rootPath, "documents"));
        var tool = CreateTool(allowDelete: false);

        using var result = JsonDocument.Parse(
            await tool.ListAsync(".", CancellationToken.None));
        var names = result.RootElement.GetProperty("entries")
            .EnumerateArray()
            .Select(static entry => entry.GetProperty("name").GetString())
            .ToArray();

        names.Should().Equal("documents");
    }

    /// <summary>
    /// Verifies create, write, read, list, move, rename, extension, and gated delete behavior.
    /// </summary>
    [Fact]
    public async Task OperationsRemainInsideSandboxAndReturnCompactJson()
    {
        Directory.CreateDirectory(_rootPath);
        var blockedTool = CreateTool(allowDelete: false);

        await blockedTool.CreateDirectoryAsync("reports", CancellationToken.None);
        var writeJson = await blockedTool.WriteAsync(
            "reports/report.txt",
            "hello",
            cancellationToken: CancellationToken.None);
        File.SetLastWriteTimeUtc(
            Path.Combine(_rootPath, "reports", "report.txt"),
            new DateTime(2026, 8, 12, 10, 30, 0, DateTimeKind.Utc));
        File.SetCreationTimeUtc(
            Path.Combine(_rootPath, "reports", "report.txt"),
            new DateTime(2026, 8, 10, 9, 15, 0, DateTimeKind.Utc));
        var readJson = await blockedTool.ReadAsync(
            "reports/report.txt",
            CancellationToken.None);
        var listJson = await blockedTool.ListAsync("reports", CancellationToken.None);
        await blockedTool.MoveAsync(
            "reports/report.txt",
            "reports/moved.txt",
            CancellationToken.None);
        var renameJson = await blockedTool.RenameAsync(
            "reports/moved.txt",
            "renamed.txt",
            CancellationToken.None);
        var extensionJson = await blockedTool.SetExtensionAsync(
            "reports/renamed.txt",
            "md",
            CancellationToken.None);

        JsonDocument.Parse(writeJson).RootElement.GetProperty("bytes").GetInt32()
            .Should().Be(5);
        JsonDocument.Parse(readJson).RootElement.GetProperty("content").GetString()
            .Should().Be("hello");
        using var list = JsonDocument.Parse(listJson);
        list.RootElement.GetProperty("entries").GetArrayLength().Should().Be(1);
        list.RootElement.GetProperty("entries")[0]
            .GetProperty("createdUtc")
            .GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 8, 10, 9, 15, 0, TimeSpan.Zero));
        list.RootElement.GetProperty("entries")[0]
            .GetProperty("lastModifiedUtc")
            .GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 8, 12, 10, 30, 0, TimeSpan.Zero));
        JsonDocument.Parse(renameJson).RootElement.GetProperty("renamed").GetBoolean()
            .Should().BeTrue();
        JsonDocument.Parse(extensionJson).RootElement.GetProperty("extension").GetString()
            .Should().Be(".md");
        File.Exists(Path.Combine(_rootPath, "reports", "renamed.md")).Should().BeTrue();
        var deleteJson = await blockedTool.DeleteAsync(
            "reports/renamed.md",
            CancellationToken.None);
        JsonDocument.Parse(deleteJson).RootElement.GetProperty("deleted").GetBoolean()
            .Should().BeTrue();
    }

    /// <summary>
    /// Verifies overwrite, atomic create-new, and exact append semantics.
    /// </summary>
    [Fact]
    public async Task WriteSupportsExplicitDestinationModes()
    {
        Directory.CreateDirectory(_rootPath);
        var tool = CreateTool(allowDelete: false);

        await tool.WriteAsync(
            "report.txt",
            "first",
            cancellationToken: CancellationToken.None);
        var overwriteJson = await tool.WriteAsync(
            "report.txt",
            "second",
            "overwrite",
            CancellationToken.None);
        var createExisting = () => tool.WriteAsync(
            "report.txt",
            "blocked",
            "createNew",
            CancellationToken.None);

        await createExisting.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(Path.Combine(_rootPath, "report.txt")))
            .Should().Be("second");

        await tool.WriteAsync(
            "created.txt",
            "created",
            "createNew",
            CancellationToken.None);
        var appendJson = await tool.WriteAsync(
            "created.txt",
            "+appended",
            "append",
            CancellationToken.None);
        await tool.WriteAsync(
            "append-created.txt",
            "no-newline",
            "append",
            CancellationToken.None);

        (await File.ReadAllTextAsync(Path.Combine(_rootPath, "created.txt")))
            .Should().Be("created+appended");
        (await File.ReadAllTextAsync(Path.Combine(_rootPath, "append-created.txt")))
            .Should().Be("no-newline");
        JsonDocument.Parse(overwriteJson).RootElement.GetProperty("mode").GetString()
            .Should().Be("overwrite");
        JsonDocument.Parse(appendJson).RootElement.GetProperty("bytes").GetInt32()
            .Should().Be(9);
    }

    /// <summary>
    /// Verifies an unsupported write mode is rejected before creating a destination directory.
    /// </summary>
    [Fact]
    public async Task WriteRejectsUnsupportedModeBeforeFileSystemChanges()
    {
        Directory.CreateDirectory(_rootPath);
        var tool = CreateTool(allowDelete: false);
        var action = () => tool.WriteAsync(
            "new/report.txt",
            "content",
            "replace",
            CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*overwrite*createNew*append*");
        Directory.Exists(Path.Combine(_rootPath, "new")).Should().BeFalse();
    }

    /// <summary>
    /// Verifies SHA-256 hashing and hash-based comparison return deterministic values.
    /// </summary>
    [Fact]
    public async Task ComputeHashAndCompareByHashReturnStableSha256Results()
    {
        Directory.CreateDirectory(_rootPath);
        var tool = CreateTool(allowDelete: false);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "a.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "b.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "c.txt"), "world");

        var hashJson = await tool.ComputeHashAsync("a.txt", CancellationToken.None);
        var equalJson = await tool.CompareByHashAsync("a.txt", "b.txt", CancellationToken.None);
        var differentJson = await tool.CompareByHashAsync("a.txt", "c.txt", CancellationToken.None);

        using var hash = JsonDocument.Parse(hashJson);
        hash.RootElement.GetProperty("algorithm").GetString().Should().Be("SHA-256");
        hash.RootElement.GetProperty("hash").GetString()
            .Should().Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
        hash.RootElement.GetProperty("fileSize").GetInt64().Should().Be(5);

        using var equal = JsonDocument.Parse(equalJson);
        equal.RootElement.GetProperty("isEqual").GetBoolean().Should().BeTrue();
        equal.RootElement.GetProperty("hashA").GetString()
            .Should().Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
        equal.RootElement.GetProperty("hashB").GetString()
            .Should().Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");

        using var different = JsonDocument.Parse(differentJson);
        different.RootElement.GetProperty("isEqual").GetBoolean().Should().BeFalse();
        different.RootElement.GetProperty("hashB").GetString()
            .Should().Be("486ea46224d1bb4fb680f34f7c9ad96a8f24ec88be73ea8e5a6c65260e9cb8a7");
    }

    /// <summary>
    /// Verifies moving to the Recycle Bin requires both write authorization and its dedicated Safety gate.
    /// </summary>
    [Fact]
    public async Task MoveToBinRequiresWriteAuthorization()
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, "discard.txt");
        await File.WriteAllTextAsync(path, "discard");
        var recycleBin = new Mock<IRecycleBinService>(MockBehavior.Strict);
        recycleBin
            .Setup(instance => instance.MoveToBinAsync(path, CancellationToken.None))
            .Returns(Task.CompletedTask);
        var allowedTool = CreateTool(
            allowDelete: false,
            allowMoveToBin: true,
            recycleBinService: recycleBin.Object);
        var json = await allowedTool.MoveToBinAsync(
            "discard.txt",
            CancellationToken.None);

        JsonDocument.Parse(json).RootElement.GetProperty("movedToBin")
            .GetBoolean().Should().BeTrue();
        recycleBin.VerifyAll();
    }

    /// <summary>
    /// Verifies process execution is disabled by default and PowerShell can return output when enabled.
    /// </summary>
    [Fact]
    public async Task PowerShellCanCaptureOutput()
    {
        var allowedTool = CreateTool(allowDelete: false, allowProcessExecution: true);
        var json = await allowedTool.PowerShellAsync(
            "Write-Output 'hello'",
            cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("exitCode").GetInt32().Should().Be(0);
        document.RootElement.GetProperty("output").GetString().Should().Contain("hello");
    }

    /// <summary>
    /// Verifies incompatible output capture options are rejected before process execution.
    /// </summary>
    [Fact]
    public async Task ProcessExecutionValidatesOutputCaptureOptions()
    {
        var tool = CreateTool(allowDelete: false, allowProcessExecution: true);

        var detachedCapture = () => tool.PowerShellAsync(
            "Write-Output 'hello'",
            waitForExit: false,
            captureOutput: true,
            cancellationToken: CancellationToken.None);
        var shellCapture = () => tool.ShellExecuteAsync(
            "https://example.com",
            captureOutput: true,
            cancellationToken: CancellationToken.None);

        await detachedCapture.Should().ThrowAsync<ArgumentException>();
        await shellCapture.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies Markdown conversion writes standalone themed HTML with raw HTML disabled.
    /// </summary>
    [Fact]
    public async Task MarkdownToHtmlWritesDerivedDarkDocumentAndSupportsLightTheme()
    {
        Directory.CreateDirectory(_rootPath);
        var tool = CreateTool(allowDelete: false);
        await File.WriteAllTextAsync(
            Path.Combine(_rootPath, "report.md"),
            "# Report\n\n**Safe** <script>alert(1)</script>");

        var darkJson = await tool.MarkdownToHtmlAsync(
            "report.md",
            cancellationToken: CancellationToken.None);
        var lightJson = await tool.MarkdownToHtmlAsync(
            "report.md",
            "light.html",
            MarkdownHtmlTheme.Light,
            CancellationToken.None);

        var dark = await File.ReadAllTextAsync(Path.Combine(_rootPath, "report.html"));
        var light = await File.ReadAllTextAsync(Path.Combine(_rootPath, "light.html"));
        dark.Should().Contain("color-scheme:dark").And.Contain(">Report</h1>");
        dark.Should().NotContain("<script>");
        light.Should().Contain("color-scheme:light");
        JsonDocument.Parse(darkJson).RootElement.GetProperty("theme").GetString()
            .Should().Be("Dark");
        JsonDocument.Parse(lightJson).RootElement.GetProperty("destinationPath").GetString()
            .Should().Be("light.html");
    }

    /// <summary>
    /// Verifies Markdown conversion authorizes its source and destination independently.
    /// </summary>
    [Fact]
    public async Task MarkdownToHtmlRequiresReadSourceAndWriteDestination()
    {
        Directory.CreateDirectory(_rootPath);
        var sourceRoot = Path.Combine(_authorizedPath, "source");
        var destinationRoot = Path.Combine(_authorizedPath, "destination");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destinationRoot);
        var sourcePath = Path.Combine(sourceRoot, "report.md");
        var destinationPath = Path.Combine(destinationRoot, "report.html");
        await File.WriteAllTextAsync(sourcePath, "# Authorized");
        var readOnlyDestination = new ToolExecutionContext(
            "conversion",
            [
                new FileSystemAccessRule
                {
                    Path = sourceRoot,
                    Access = FileSystemAccessMode.ReadOnly,
                },
                new FileSystemAccessRule
                {
                    Path = destinationRoot,
                    Access = FileSystemAccessMode.ReadOnly,
                },
            ]);
        var blockedTool = CreateTool(allowDelete: false, readOnlyDestination);
        var blocked = () => blockedTool.MarkdownToHtmlAsync(
            sourcePath,
            destinationPath,
            cancellationToken: CancellationToken.None);

        await blocked.Should().ThrowAsync<PathSandboxException>()
            .WithMessage("*read-only*");

        var allowedTool = CreateTool(
            allowDelete: false,
            new ToolExecutionContext(
                "conversion",
                [
                    new FileSystemAccessRule
                    {
                        Path = sourceRoot,
                        Access = FileSystemAccessMode.ReadOnly,
                    },
                    new FileSystemAccessRule
                    {
                        Path = destinationRoot,
                        Access = FileSystemAccessMode.ReadWrite,
                    },
                ]));
        _ = await allowedTool.MarkdownToHtmlAsync(
            sourcePath,
            destinationPath,
            cancellationToken: CancellationToken.None);

        File.Exists(destinationPath).Should().BeTrue();
    }

    /// <summary>
    /// Verifies HTML extraction defaults to plain text and can preserve principal Markdown structure.
    /// </summary>
    [Fact]
    public async Task HtmlToTextReturnsPlainTextOrStructuredMarkdown()
    {
        Directory.CreateDirectory(_rootPath);
        var tool = CreateTool(allowDelete: false);
        await File.WriteAllTextAsync(
            Path.Combine(_rootPath, "page.html"),
            "<html><body><h1>Title</h1><p>Hello <strong>world</strong>.</p><ul><li>One</li><li>Two</li></ul><script>bad()</script></body></html>");

        var textJson = await tool.HtmlToTextAsync(
            "page.html",
            cancellationToken: CancellationToken.None);
        var markdownJson = await tool.HtmlToTextAsync(
            "page.html",
            HtmlTextFormat.Markdown,
            CancellationToken.None);
        var text = JsonDocument.Parse(textJson).RootElement.GetProperty("content").GetString();
        var markdown = JsonDocument.Parse(markdownJson).RootElement.GetProperty("content").GetString();

        text.Should().Contain("Title").And.Contain("Hello world.").And.NotContain("bad()");
        markdown.Should().Contain("# Title").And.Contain("**world**").And.Contain("- One");
    }

    /// <summary>
    /// Verifies document conversions enforce the existing source read-size cap.
    /// </summary>
    [Fact]
    public async Task DocumentConversionsEnforceReadSizeCap()
    {
        Directory.CreateDirectory(_rootPath);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "large.md"), "12345");
        var fileSystemOptions = Options.Create(
            new FileSystemOptions { RootPath = _rootPath, MaxReadBytes = 4 });
        var tool = new FileSystemTool(
            new PathSandbox(fileSystemOptions),
            fileSystemOptions,
            Mock.Of<IRecycleBinService>(),
            NullLogger<FileSystemTool>.Instance,
            TimeProvider.System,
            ToolExecutionContext.Taskless);
        var action = () => tool.MarkdownToHtmlAsync(
            "large.md",
            cancellationToken: CancellationToken.None);

        await action.Should().ThrowAsync<IOException>()
            .WithMessage("*read limit*");
    }

    /// <summary>
    /// Verifies rename and extension arguments cannot introduce a directory path.
    /// </summary>
    [Fact]
    public async Task RenameAndSetExtensionRejectPathInjection()
    {
        Directory.CreateDirectory(_rootPath);
        var tool = CreateTool(allowDelete: false);
        await tool.WriteAsync(
            "source.txt",
            "content",
            cancellationToken: CancellationToken.None);

        var rename = () => tool.RenameAsync(
            "source.txt",
            "../escaped.txt",
            CancellationToken.None);
        var extension = () => tool.SetExtensionAsync(
            "source.txt",
            "../md",
            CancellationToken.None);

        await rename.Should().ThrowAsync<ArgumentException>();
        await extension.Should().ThrowAsync<ArgumentException>();
        File.Exists(Path.Combine(_rootPath, "source.txt")).Should().BeTrue();
    }

    /// <summary>
    /// Verifies rooted paths and parent traversal are rejected.
    /// </summary>
    [Fact]
    public async Task SandboxEscapeIsRejected()
    {
        Directory.CreateDirectory(_rootPath);
        var tool = CreateTool(allowDelete: true);

        var parentTraversal = () => tool.ReadAsync(
            "../outside.txt",
            CancellationToken.None);
        var rootedPath = () => tool.ReadAsync(
            Path.Combine(_rootPath, "inside.txt"),
            CancellationToken.None);

        await parentTraversal.Should().ThrowAsync<PathSandboxException>();
        await rootedPath.Should().ThrowAsync<PathSandboxException>();
    }

    /// <summary>
    /// Verifies task rules authorize absolute reads and enforce the most-specific access mode.
    /// </summary>
    [Fact]
    public async Task AbsolutePathsRequireTaskAuthorizationAndSpecificAccess()
    {
        Directory.CreateDirectory(_rootPath);
        var writableChild = Path.Combine(_authorizedPath, "writable");
        Directory.CreateDirectory(writableChild);
        var context = new ToolExecutionContext(
            "absolute-access",
            [
                new FileSystemAccessRule
                {
                    Path = _authorizedPath,
                    Access = FileSystemAccessMode.ReadOnly,
                },
                new FileSystemAccessRule
                {
                    Path = writableChild,
                    Access = FileSystemAccessMode.ReadWrite,
                },
            ]);
        var tool = CreateTool(allowDelete: false, context);

        await tool.WriteAsync(
            Path.Combine(writableChild, "allowed.txt"),
            "allowed",
            cancellationToken: CancellationToken.None);
        var readJson = await tool.ReadAsync(
            Path.Combine(writableChild, "allowed.txt"),
            CancellationToken.None);
        var readOnlyWrite = () => tool.WriteAsync(
            Path.Combine(_authorizedPath, "blocked.txt"),
            "blocked",
            cancellationToken: CancellationToken.None);
        var unauthorizedWrite = () => tool.WriteAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "blocked.txt"),
            "blocked",
            cancellationToken: CancellationToken.None);
        var siblingPrefixWrite = () => tool.WriteAsync(
            _authorizedPath + "-sibling" + Path.DirectorySeparatorChar + "blocked.txt",
            "blocked",
            cancellationToken: CancellationToken.None);

        JsonDocument.Parse(readJson).RootElement.GetProperty("content").GetString()
            .Should().Be("allowed");
        await readOnlyWrite.Should().ThrowAsync<PathSandboxException>()
            .WithMessage("*read-only*");
        await unauthorizedWrite.Should().ThrowAsync<PathSandboxException>()
            .WithMessage("*no task authorization*");
        await siblingPrefixWrite.Should().ThrowAsync<PathSandboxException>()
            .WithMessage("*no task authorization*");
    }

    /// <summary>
    /// Verifies move validates source and destination write authorization independently.
    /// </summary>
    [Fact]
    public async Task AbsoluteMoveRequiresWriteAccessAtBothLocations()
    {
        Directory.CreateDirectory(_rootPath);
        var sourceRoot = Path.Combine(_authorizedPath, "source");
        var destinationRoot = Path.Combine(_authorizedPath, "destination");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destinationRoot);
        var sourcePath = Path.Combine(sourceRoot, "move.txt");
        var destinationPath = Path.Combine(destinationRoot, "move.txt");
        await File.WriteAllTextAsync(sourcePath, "move");
        var readOnlyDestinationContext = new ToolExecutionContext(
            "move-task",
            [
                new FileSystemAccessRule
                {
                    Path = sourceRoot,
                    Access = FileSystemAccessMode.ReadWrite,
                },
                new FileSystemAccessRule
                {
                    Path = destinationRoot,
                    Access = FileSystemAccessMode.ReadOnly,
                },
            ]);
        var blockedTool = CreateTool(
            allowDelete: false,
            readOnlyDestinationContext);
        var blockedAction = () => blockedTool.MoveAsync(
            sourcePath,
            destinationPath,
            CancellationToken.None);

        await blockedAction.Should().ThrowAsync<PathSandboxException>()
            .WithMessage("*read-only*");

        var allowedTool = CreateTool(
            allowDelete: false,
            new ToolExecutionContext(
                "move-task",
                [
                    new FileSystemAccessRule
                    {
                        Path = sourceRoot,
                        Access = FileSystemAccessMode.ReadWrite,
                    },
                    new FileSystemAccessRule
                    {
                        Path = destinationRoot,
                        Access = FileSystemAccessMode.ReadWrite,
                    },
                ]));
        _ = await allowedTool.MoveAsync(
            sourcePath,
            destinationPath,
            CancellationToken.None);

        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(destinationPath).Should().BeTrue();
    }

    /// <summary>
    /// Verifies absolute rename and extension changes require task read-write authorization.
    /// </summary>
    [Fact]
    public async Task AbsoluteRenameAndSetExtensionRequireWriteAccess()
    {
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_authorizedPath);
        var sourcePath = Path.Combine(_authorizedPath, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "content");
        var readOnlyTool = CreateTool(
            allowDelete: false,
            new ToolExecutionContext(
                "read-only-rename",
                [
                    new FileSystemAccessRule
                    {
                        Path = _authorizedPath,
                        Access = FileSystemAccessMode.ReadOnly,
                    },
                ]));
        var blocked = () => readOnlyTool.RenameAsync(
            sourcePath,
            "blocked.txt",
            CancellationToken.None);

        await blocked.Should().ThrowAsync<PathSandboxException>()
            .WithMessage("*read-only*");

        var writableTool = CreateTool(
            allowDelete: false,
            CreateContext("writable-rename", _authorizedPath));
        _ = await writableTool.RenameAsync(
            sourcePath,
            "renamed.txt",
            CancellationToken.None);
        _ = await writableTool.SetExtensionAsync(
            Path.Combine(_authorizedPath, "renamed.txt"),
            ".md",
            CancellationToken.None);

        File.Exists(Path.Combine(_authorizedPath, "renamed.md")).Should().BeTrue();
    }

    /// <summary>
    /// Verifies independent contextual tools do not share absolute-path grants.
    /// </summary>
    [Fact]
    public async Task ConcurrentTaskContextsRemainIsolated()
    {
        Directory.CreateDirectory(_rootPath);
        var firstRoot = Path.Combine(_authorizedPath, "first");
        var secondRoot = Path.Combine(_authorizedPath, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var firstTool = CreateTool(
            allowDelete: false,
            CreateContext("first-task", firstRoot));
        var secondTool = CreateTool(
            allowDelete: false,
            CreateContext("second-task", secondRoot));

        await Task.WhenAll(
            firstTool.WriteAsync(
                Path.Combine(firstRoot, "first.txt"),
                "first",
                cancellationToken: CancellationToken.None),
            secondTool.WriteAsync(
                Path.Combine(secondRoot, "second.txt"),
                "second",
                cancellationToken: CancellationToken.None));
        var firstCrossAccess = () => firstTool.ReadAsync(
            Path.Combine(secondRoot, "second.txt"),
            CancellationToken.None);
        var secondCrossAccess = () => secondTool.ReadAsync(
            Path.Combine(firstRoot, "first.txt"),
            CancellationToken.None);

        await firstCrossAccess.Should().ThrowAsync<PathSandboxException>();
        await secondCrossAccess.Should().ThrowAsync<PathSandboxException>();
    }

    /// <summary>
    /// Verifies absolute deletion requires both task write access and the Safety gate.
    /// </summary>
    [Fact]
    public async Task AbsoluteDeleteRequiresLocationAuthorization()
    {
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_authorizedPath);
        var filePath = Path.Combine(_authorizedPath, "delete.txt");
        await File.WriteAllTextAsync(filePath, "delete");
        var context = CreateContext("delete-task", _authorizedPath);
        var tool = CreateTool(allowDelete: true, context);
        _ = await tool.DeleteAsync(filePath, CancellationToken.None);

        File.Exists(filePath).Should().BeFalse();
    }

    /// <summary>
    /// Verifies an existing reparse point cannot redirect an authorized operation.
    /// </summary>
    [Fact]
    public async Task ReparsePointEscapeIsRejectedWhenSupported()
    {
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_authorizedPath);
        var outsidePath = Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Outside",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsidePath);
        var linkPath = Path.Combine(_authorizedPath, "link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, outsidePath);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            Directory.Delete(outsidePath, recursive: true);
            return;
        }

        try
        {
            var tool = CreateTool(
                allowDelete: false,
                CreateContext("reparse-task", _authorizedPath));
            var action = () => tool.WriteAsync(
                Path.Combine(linkPath, "escaped.txt"),
                "blocked",
                cancellationToken: CancellationToken.None);

            await action.Should().ThrowAsync<PathSandboxException>()
                .WithMessage("*reparse point*");
        }
        finally
        {
            Directory.Delete(linkPath);
            Directory.Delete(outsidePath, recursive: true);
        }
    }

    /// <summary>
    /// Deletes the isolated test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        if (Directory.Exists(_authorizedPath))
        {
            Directory.Delete(_authorizedPath, recursive: true);
        }
    }

    private FileSystemTool CreateTool(
        bool allowDelete,
        ToolExecutionContext? context = null,
        bool allowMoveToBin = false,
        IRecycleBinService? recycleBinService = null,
        bool allowProcessExecution = false)
    {
        var fileSystemOptions = Options.Create(
            new FileSystemOptions { RootPath = _rootPath });
        return new FileSystemTool(
            new PathSandbox(fileSystemOptions),
            fileSystemOptions,
            recycleBinService ?? Mock.Of<IRecycleBinService>(),
            NullLogger<FileSystemTool>.Instance,
            TimeProvider.System,
            context ?? ToolExecutionContext.Taskless);
    }

    private static ToolExecutionContext CreateContext(
        string taskId,
        string path) =>
        new(
            taskId,
            [
                new FileSystemAccessRule
                {
                    Path = path,
                    Access = FileSystemAccessMode.ReadWrite,
                },
            ]);
}
