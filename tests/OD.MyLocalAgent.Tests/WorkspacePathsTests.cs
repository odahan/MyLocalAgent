using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies deterministic application-owned path resolution inside a workspace.
/// </summary>
public sealed class WorkspacePathsTests : IDisposable
{
    private readonly string _workspaceDirectory = Path.Combine(
        Path.GetTempPath(),
        "OD.MyLocalAgent.Tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies every durable application path stays under the central directory.
    /// </summary>
    [Fact]
    public void ResolvesCentralWorkspaceLayout()
    {
        var paths = new WorkspacePaths(_workspaceDirectory);
        var applicationDirectory = Path.Combine(
            Path.GetFullPath(_workspaceDirectory),
            WorkspacePaths.ApplicationDirectoryName);

        paths.ApplicationDataDirectory.Should().Be(applicationDirectory);
        paths.TasksDirectory.Should().Be(Path.Combine(applicationDirectory, "tasks"));
        paths.TaskBackupsDirectory.Should().Be(
            Path.Combine(applicationDirectory, "tasks", "backups"));
        paths.TaskExamplesDirectory.Should().Be(
            Path.Combine(applicationDirectory, "examples", "tasks"));
        paths.DirectChatDatabasePath.Should().Be(
            Path.Combine(applicationDirectory, "data", "direct-chat.db"));
        paths.KnowledgeDirectory.Should().Be(
            Path.Combine(applicationDirectory, "data", "knowledge"));
        paths.KnowledgeDatabasePath.Should().Be(
            Path.Combine(paths.KnowledgeDirectory, "knowledge.db"));
        paths.KnowledgeDocumentsDirectory.Should().Be(
            Path.Combine(paths.KnowledgeDirectory, "documents"));
        paths.KnowledgeStagingDirectory.Should().Be(
            Path.Combine(paths.KnowledgeDirectory, "staging"));
        paths.ApplicationLogsDirectory.Should().Be(
            Path.Combine(applicationDirectory, "logs", "application"));
        paths.RunLogsDirectory.Should().Be(
            Path.Combine(applicationDirectory, "logs", "runs"));
        paths.IncidentsPath.Should().Be(
            Path.Combine(applicationDirectory, "logs", "incidents.jsonl"));
        paths.ReportsDirectory.Should().Be(
            Path.Combine(applicationDirectory, "outputs", "reports"));
        paths.ReportIndexPath.Should().Be(
            Path.Combine(paths.ReportsDirectory, "index.jsonl"));
    }

    /// <summary>
    /// Verifies standard directories are created without creating data files.
    /// </summary>
    [Fact]
    public void EnsureDirectoriesCreatesStandardLayout()
    {
        var paths = new WorkspacePaths(_workspaceDirectory);

        paths.EnsureDirectories();

        Directory.Exists(paths.TasksDirectory).Should().BeTrue();
        Directory.Exists(paths.TaskBackupsDirectory).Should().BeTrue();
        Directory.Exists(paths.TaskExamplesDirectory).Should().BeTrue();
        Directory.Exists(paths.DataDirectory).Should().BeTrue();
        Directory.Exists(paths.KnowledgeDocumentsDirectory).Should().BeTrue();
        Directory.Exists(paths.KnowledgeStagingDirectory).Should().BeTrue();
        Directory.Exists(paths.ApplicationLogsDirectory).Should().BeTrue();
        Directory.Exists(paths.RunLogsDirectory).Should().BeTrue();
        Directory.Exists(paths.ReportsDirectory).Should().BeTrue();
        if (OperatingSystem.IsWindows())
        {
            File.GetAttributes(paths.ApplicationDataDirectory)
                .Should().HaveFlag(FileAttributes.Hidden);
        }

        File.Exists(paths.DirectChatDatabasePath).Should().BeFalse();
        File.Exists(paths.IncidentsPath).Should().BeFalse();
    }

    /// <summary>
    /// Verifies a relative workspace can never produce ambiguous durable paths.
    /// </summary>
    [Fact]
    public void RejectsRelativeWorkspace()
    {
        var action = () => new WorkspacePaths("relative-workspace");

        action.Should().Throw<ArgumentException>()
            .WithMessage("*absolute path*");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_workspaceDirectory))
        {
            Directory.Delete(_workspaceDirectory, recursive: true);
        }
    }
}
