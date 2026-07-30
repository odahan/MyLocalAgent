using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies scheduler entry assistance in the task editor.
/// </summary>
public sealed class TaskEditorViewModelTests
{
    /// <summary>
    /// Verifies selecting a plain-language preset applies its cron expression.
    /// </summary>
    [Fact]
    public void SelectingCronPresetUpdatesScheduleAndPreview()
    {
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System);
        var preset = viewModel.CronPresets.Single(
            item => item.Label == "Weekdays at 09:00");

        viewModel.SelectedCronPreset = preset;

        viewModel.Schedule.Should().Be("0 9 * * 1-5");
        viewModel.IsCronValid.Should().BeTrue();
        viewModel.CronDescription.Should().Be("Weekdays at 09:00");
        viewModel.CronPreviewText.Should().Contain("•");
    }

    /// <summary>
    /// Verifies a selected folder is added and persisted with the task.
    /// </summary>
    [Fact]
    public async Task AuthorizedLocationIsAddedAndSaved()
    {
        var authorizedPath = Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Editor",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(authorizedPath);
        try
        {
            var store = new Mock<ITaskStore>(MockBehavior.Strict);
            store.Setup(
                    instance => instance.GetAsync(
                        "editor-task",
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync((ScheduledTask?)null);
            ScheduledTask? savedTask = null;
            store.Setup(
                    instance => instance.SaveAsync(
                        It.IsAny<ScheduledTask>(),
                        It.IsAny<CancellationToken>()))
                .Callback<ScheduledTask, CancellationToken>(
                    (task, _) => savedTask = task)
                .Returns(Task.CompletedTask);
            var scheduler = new Mock<ITaskSchedulerService>(MockBehavior.Strict);
            scheduler.Setup(
                    instance => instance.RegisterAsync(
                        It.IsAny<ScheduledTask>(),
                        It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
            desktop.Setup(
                    instance => instance.SelectFolder(
                        "Select an authorized task location"))
                .Returns(authorizedPath);
            var viewModel = new TaskEditorViewModel(
                store.Object,
                scheduler.Object,
                Mock.Of<IIncidentReporter>(),
                desktop.Object,
                Mock.Of<IMessenger>(),
                TimeProvider.System)
            {
                Id = "editor-task",
                Name = "Editor task",
                Prompt = "Write a report.",
                UseFileSystem = true,
            };

            viewModel.AddAuthorizedLocationCommand.Execute(null);
            viewModel.AuthorizedLocations.Single().CanWrite = false;
            await viewModel.SaveCommand.ExecuteAsync(null);

            viewModel.AuthorizedLocations.Should().ContainSingle();
            savedTask.Should().NotBeNull();
            savedTask!.FileSystemAccess.Should().ContainSingle(
                rule =>
                    rule.Path == authorizedPath &&
                    rule.Access == FileSystemAccessMode.ReadOnly);
            store.VerifyAll();
            scheduler.VerifyAll();
            desktop.VerifyAll();
        }
        finally
        {
            Directory.Delete(authorizedPath, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an authorized location can be removed before saving.
    /// </summary>
    [Fact]
    public void AuthorizedLocationCanBeRemoved()
    {
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System);
        var item = new FileSystemAccessItem(
            Path.Combine(Path.GetTempPath(), "remove"),
            FileSystemAccessMode.ReadWrite);
        viewModel.AuthorizedLocations.Add(item);

        viewModel.RemoveAuthorizedLocationCommand.Execute(item);

        viewModel.AuthorizedLocations.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the guided workflow editor inserts a usable prompt-step skeleton.
    /// </summary>
    [Fact]
    public void GuidedEditorAddsAndFormatsPromptStep()
    {
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System)
        {
            SelectedWorkflowAlias = ToolMap.Find(ToolMap.PromptAlias),
        };

        viewModel.AddWorkflowStepCommand.Execute(null);

        viewModel.WorkflowText.Should().Contain("\"step\": \"ai.prompt\"");
        viewModel.WorkflowText.Should().Contain("\"includeContext\": true");
        viewModel.ValidationText.Should().Contain("added");
    }

}
