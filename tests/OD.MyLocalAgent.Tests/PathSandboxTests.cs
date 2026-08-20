using FluentAssertions;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies protection of application-owned workspace data.
/// </summary>
public sealed class PathSandboxTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OD.MyLocalAgent.PathSandbox",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies direct access remains blocked even when an explicit rule grants it.
    /// </summary>
    [Fact]
    public void ApplicationOwnedDataCannotBeReadOrWrittenByManagedTools()
    {
        var sandbox = CreateSandbox();
        var applicationData = Path.Combine(
            _workspace,
            WorkspacePaths.ApplicationDirectoryName);
        var explicitContext = new ToolExecutionContext(
            "reserved-access",
            [
                new FileSystemAccessRule
                {
                    Path = applicationData,
                    Access = FileSystemAccessMode.ReadWrite,
                },
            ]);

        var relativeRead = () => sandbox.Resolve(
            $"{WorkspacePaths.ApplicationDirectoryName}/tasks/task.json",
            FileSystemAccessMode.ReadOnly,
            ToolExecutionContext.Taskless);
        var absoluteRead = () => sandbox.Resolve(
            Path.Combine(applicationData, "tasks", "task.json"),
            FileSystemAccessMode.ReadOnly,
            explicitContext);

        relativeRead.Should().Throw<PathSandboxException>()
            .WithMessage("*reserved for the application*");
        absoluteRead.Should().Throw<PathSandboxException>()
            .WithMessage("*reserved for the application*");
        sandbox.IsApplicationOwnedPath(applicationData).Should().BeTrue();
        sandbox.IsApplicationOwnedPath(Path.Combine(applicationData, "data", "knowledge", "knowledge.db")).Should().BeTrue();
        sandbox.IsApplicationOwnedPath(Path.Combine(_workspace, "documents"))
            .Should().BeFalse();
    }

    /// <summary>
    /// Verifies destructive and recursive operations cannot target a protected ancestor.
    /// </summary>
    [Fact]
    public void ProtectedAncestorsRejectWritesAndRecursiveReads()
    {
        var sandbox = CreateSandbox();

        sandbox.Resolve(
                ".",
                FileSystemAccessMode.ReadOnly,
                ToolExecutionContext.Taskless)
            .Should().Be(Path.GetFullPath(_workspace));
        var write = () => sandbox.Resolve(
            ".",
            FileSystemAccessMode.ReadWrite,
            ToolExecutionContext.Taskless);
        var recursiveRead = () => sandbox.EnsureNoApplicationOwnedDescendant(
            Path.GetFullPath(_workspace));

        write.Should().Throw<PathSandboxException>()
            .WithMessage("*could modify application-owned data*");
        recursiveRead.Should().Throw<PathSandboxException>()
            .WithMessage("*recursive access*");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            File.SetAttributes(_workspace, FileAttributes.Normal);
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private PathSandbox CreateSandbox()
    {
        Directory.CreateDirectory(_workspace);
        return new PathSandbox(
            Options.Create(new FileSystemOptions { RootPath = _workspace }));
    }
}
