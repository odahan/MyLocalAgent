using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies validated task persistence and mixed workflow parsing.
/// </summary>
public sealed class JsonTaskStoreTests : IDisposable
{
    private readonly string _directoryPath =
        Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies CRUD round-trip behavior for one-file-per-task persistence.
    /// </summary>
    [Fact]
    public async Task SaveGetListAndDeleteRoundTrip()
    {
        using var store = new JsonTaskStore(_directoryPath, TimeProvider.System);
        var task = ScheduledTaskSerializer.Deserialize(ValidMixedWorkflowJson);
        var changes = new List<TaskStoreChangeKind>();
        store.TasksChanged += (_, args) =>
        {
            if (args.ChangeKind is TaskStoreChangeKind.Saved or TaskStoreChangeKind.Deleted)
            {
                changes.Add(args.ChangeKind);
            }
        };

        await store.SaveAsync(task, CancellationToken.None);
        var loaded = await store.GetAsync(task.Id, CancellationToken.None);
        var all = await store.GetAllAsync(CancellationToken.None);
        var deleted = await store.DeleteAsync(task.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("mixed-task");
        loaded.FileSystemAccess.Should().ContainSingle();
        loaded.FileSystemAccess[0].Access.Should().Be(FileSystemAccessMode.ReadWrite);
        loaded.Workflow.Should().HaveCount(3);
        loaded.Workflow[0].IsStringForm.Should().BeTrue();
        loaded.Workflow[1].Args.GetProperty("path").GetString()
            .Should().Be("reports/out.md");
        all.Should().ContainSingle(item => item.Id == "mixed-task");
        deleted.Should().BeTrue();
        changes.Should().ContainInOrder(
            TaskStoreChangeKind.Saved,
            TaskStoreChangeKind.Deleted);
    }

    /// <summary>
    /// Verifies invalid task JSON fails validation when loaded.
    /// </summary>
    [Fact]
    public async Task InvalidTaskFailsValidationOnLoad()
    {
        Directory.CreateDirectory(_directoryPath);
        await File.WriteAllTextAsync(
            Path.Combine(_directoryPath, "bad.json"),
            """
            {
              "id": "Bad Id",
              "name": "Bad",
              "schedule": "not cron",
              "prompt": "",
              "tools": [],
              "workflow": [],
              "enabled": true,
              "errorPolicy": "StopOnError"
            }
            """);
        using var store = new JsonTaskStore(_directoryPath, TimeProvider.System);

        var action = () => store.GetAllAsync(CancellationToken.None);

        await action.Should().ThrowAsync<TaskValidationException>();
    }

    /// <summary>
    /// Verifies string and object workflow forms normalize into reusable step models.
    /// </summary>
    [Fact]
    public void MixedWorkflowFormsParseIntoNormalizedSteps()
    {
        var task = ScheduledTaskSerializer.Deserialize(ValidMixedWorkflowJson);

        task.Workflow[0].Step.Should().Be("gmail.readToday");
        task.Workflow[0].EffectiveId.Should().Be("gmail.readToday");
        task.Workflow[1].Step.Should().Be("filesystem.write");
        task.Workflow[1].EffectiveId.Should().Be("writeReport");
        task.Workflow[1].OnError.Should().Be(WorkflowErrorPolicy.Retry);
        task.Workflow[1].RetryCount.Should().Be(2);
        task.Workflow[2].Step.Should().Be("ai.summarize");
    }

    /// <summary>
    /// Verifies the Web Search group and alias are valid in persisted tasks.
    /// </summary>
    [Fact]
    public void WebSearchTaskPassesValidation()
    {
        var task = ScheduledTaskSerializer.Deserialize(
            """
            {
              "id": "web-search-task",
              "name": "Web search task",
              "schedule": "0 8 * * *",
              "prompt": "Find current information.",
              "tools": ["web"],
              "workflow": [
                {
                  "step": "web.search",
                  "args": {
                    "query": "current Ollama documentation"
                  }
                }
              ],
              "enabled": true,
              "errorPolicy": "StopOnError"
            }
            """);

        task.Tools.Should().Equal(ToolGroups.Web);
        task.Workflow.Should().ContainSingle();
        task.Workflow[0].Step.Should().Be("web.search");
    }

    /// <summary>
    /// Verifies adding Web Search does not relax rejection of unknown groups.
    /// </summary>
    [Fact]
    public void UnknownToolGroupStillFailsValidation()
    {
        var action = () => ScheduledTaskSerializer.Deserialize(
            """
            {
              "id": "unknown-group-task",
              "name": "Unknown group task",
              "schedule": "0 8 * * *",
              "prompt": "Test validation.",
              "tools": ["future"],
              "workflow": [],
              "enabled": true,
              "errorPolicy": "StopOnError"
            }
            """);

        action.Should().Throw<TaskValidationException>()
            .WithMessage("*tool group*future*unknown*");
    }

    /// <summary>
    /// Verifies task-specific file-system grants round-trip in canonical form.
    /// </summary>
    [Fact]
    public void FileSystemAccessRulesRoundTripAndNormalizePaths()
    {
        var authorizedPath = Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Tests",
            "..",
            "Authorized");
        var task = new ScheduledTask
        {
            Id = "file-access",
            Name = "File access",
            Schedule = "0 7 * * *",
            Prompt = "Read authorized files.",
            Tools = [ToolGroups.FileSystem],
            FileSystemAccess =
            [
                new FileSystemAccessRule
                {
                    Path = authorizedPath,
                    Access = FileSystemAccessMode.ReadOnly,
                },
            ],
        };

        var json = ScheduledTaskSerializer.Serialize(task);
        var roundTrip = ScheduledTaskSerializer.Deserialize(json);

        roundTrip.FileSystemAccess.Should().ContainSingle();
        roundTrip.FileSystemAccess[0].Path.Should().Be(
            Path.GetFullPath(authorizedPath));
        roundTrip.FileSystemAccess[0].Access.Should().Be(FileSystemAccessMode.ReadOnly);
    }

    /// <summary>
    /// Verifies relative, wildcard, and duplicate grants fail task validation.
    /// </summary>
    [Fact]
    public void InvalidFileSystemAccessRulesAreRejected()
    {
        var duplicatePath = Path.Combine(Path.GetTempPath(), "duplicate");
        var task = new ScheduledTask
        {
            Id = "invalid-access",
            Name = "Invalid access",
            Schedule = "0 7 * * *",
            Prompt = "Test.",
            Tools = [ToolGroups.FileSystem],
            FileSystemAccess =
            [
                new FileSystemAccessRule
                {
                    Path = duplicatePath,
                    Access = FileSystemAccessMode.ReadOnly,
                },
                new FileSystemAccessRule
                {
                    Path = duplicatePath + Path.DirectorySeparatorChar,
                    Access = FileSystemAccessMode.ReadWrite,
                },
            ],
        };

        var duplicateAction = () => ScheduledTaskValidator.Validate(task);
        var relativeAction = () => ScheduledTaskValidator.Validate(
            task with
            {
                FileSystemAccess =
                [
                    new FileSystemAccessRule
                    {
                        Path = "relative",
                        Access = FileSystemAccessMode.ReadOnly,
                    },
                ],
            });
        var wildcardAction = () => ScheduledTaskValidator.Validate(
            task with
            {
                FileSystemAccess =
                [
                    new FileSystemAccessRule
                    {
                        Path = Path.Combine(Path.GetTempPath(), "*"),
                        Access = FileSystemAccessMode.ReadOnly,
                    },
                ],
            });

        duplicateAction.Should().Throw<TaskValidationException>()
            .WithMessage("*duplicated*");
        relativeAction.Should().Throw<TaskValidationException>()
            .WithMessage("*must be absolute*");
        wildcardAction.Should().Throw<TaskValidationException>()
            .WithMessage("*wildcards*");
    }

    /// <summary>
    /// Deletes the isolated test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directoryPath))
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
    }

    private const string ValidMixedWorkflowJson =
        """
        {
          "id": "mixed-task",
          "name": "Mixed task",
          "schedule": "0 7 * * *",
          "prompt": "Summarize today's mail.",
          "tools": ["gmail", "filesystem"],
          "fileSystemAccess": [
            {
              "path": "D:\\Archive",
              "access": "ReadWrite"
            }
          ],
          "workflow": [
            "gmail.readToday",
            {
              "step": "filesystem.write",
              "id": "writeReport",
              "args": {
                "path": "reports/out.md",
                "content": "{{gmail.readToday.output}}"
              },
              "onError": "Retry",
              "retryCount": 2,
              "retryBackoffSeconds": 1
            },
            "ai.summarize"
          ],
          "enabled": true,
          "errorPolicy": "StopOnError"
        }
        """;
}
