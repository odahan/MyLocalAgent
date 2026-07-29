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
    /// Deletes the isolated test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private FileSystemTool CreateTool(bool allowDelete)
    {
        var fileSystemOptions = Options.Create(
            new FileSystemOptions { RootPath = _rootPath });
        return new FileSystemTool(
            new PathSandbox(fileSystemOptions),
            Options.Create(new SafetyOptions { AllowFileDelete = allowDelete }),
            fileSystemOptions,
            NullLogger<FileSystemTool>.Instance,
            TimeProvider.System);
    }
}
