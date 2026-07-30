using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    /// Verifies create, write, read, list, move, and gated delete behavior.
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
            CancellationToken.None);
        var readJson = await blockedTool.ReadAsync(
            "reports/report.txt",
            CancellationToken.None);
        var listJson = await blockedTool.ListAsync("reports", CancellationToken.None);
        await blockedTool.MoveAsync(
            "reports/report.txt",
            "reports/moved.txt",
            CancellationToken.None);
        var blockedDelete = () => blockedTool.DeleteAsync(
            "reports/moved.txt",
            CancellationToken.None);

        JsonDocument.Parse(writeJson).RootElement.GetProperty("bytes").GetInt32()
            .Should().Be(5);
        JsonDocument.Parse(readJson).RootElement.GetProperty("content").GetString()
            .Should().Be("hello");
        JsonDocument.Parse(listJson).RootElement.GetProperty("entries").GetArrayLength()
            .Should().Be(1);
        File.Exists(Path.Combine(_rootPath, "reports", "moved.txt")).Should().BeTrue();
        await blockedDelete.Should().ThrowAsync<SafetyException>()
            .Where(exception => exception.RequiredFlag == "Safety:AllowFileDelete");

        var allowedTool = CreateTool(allowDelete: true);
        var deleteJson = await allowedTool.DeleteAsync(
            "reports/moved.txt",
            CancellationToken.None);
        JsonDocument.Parse(deleteJson).RootElement.GetProperty("deleted").GetBoolean()
            .Should().BeTrue();
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
            CancellationToken.None);
        var readJson = await tool.ReadAsync(
            Path.Combine(writableChild, "allowed.txt"),
            CancellationToken.None);
        var readOnlyWrite = () => tool.WriteAsync(
            Path.Combine(_authorizedPath, "blocked.txt"),
            "blocked",
            CancellationToken.None);
        var unauthorizedWrite = () => tool.WriteAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "blocked.txt"),
            "blocked",
            CancellationToken.None);
        var siblingPrefixWrite = () => tool.WriteAsync(
            _authorizedPath + "-sibling" + Path.DirectorySeparatorChar + "blocked.txt",
            "blocked",
            CancellationToken.None);

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
                CancellationToken.None),
            secondTool.WriteAsync(
                Path.Combine(secondRoot, "second.txt"),
                "second",
                CancellationToken.None));
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
    public async Task AbsoluteDeleteRequiresLocationAndSafetyAuthorization()
    {
        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_authorizedPath);
        var filePath = Path.Combine(_authorizedPath, "delete.txt");
        await File.WriteAllTextAsync(filePath, "delete");
        var context = CreateContext("delete-task", _authorizedPath);
        var blockedTool = CreateTool(allowDelete: false, context);
        var blockedAction = () => blockedTool.DeleteAsync(
            filePath,
            CancellationToken.None);

        await blockedAction.Should().ThrowAsync<SafetyException>()
            .Where(exception => exception.RequiredFlag == "Safety:AllowFileDelete");

        var allowedTool = CreateTool(allowDelete: true, context);
        _ = await allowedTool.DeleteAsync(filePath, CancellationToken.None);

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
                CancellationToken.None);

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
        ToolExecutionContext? context = null)
    {
        var fileSystemOptions = Options.Create(
            new FileSystemOptions { RootPath = _rootPath });
        return new FileSystemTool(
            new PathSandbox(fileSystemOptions),
            Options.Create(new SafetyOptions { AllowFileDelete = allowDelete }),
            fileSystemOptions,
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
