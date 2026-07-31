using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
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

    /// <summary>
    /// Verifies invalid Explain input is rejected locally without opening the helper.
    /// </summary>
    [Fact]
    public void InvalidExplainInputDoesNotOpenDialog()
    {
        var dialog = new Mock<IAIWorkflowHelperDialogService>(MockBehavior.Strict);
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System,
            dialog.Object)
        {
            WorkflowText = "not json",
        };

        viewModel.OpenAIExplainCommand.Execute(null);

        viewModel.ValidationText.Should().Contain("invalid");
        dialog.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies an invalid task definition explains that Save did not persist anything.
    /// </summary>
    [Fact]
    public async Task InvalidTaskSaveShowsErrorAndKeepsDraft()
    {
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(
                service => service.ShowError(
                    "Task was not saved",
                    It.Is<string>(message =>
                        message.Contains("No changes were saved.", StringComparison.Ordinal))))
            .Verifiable();
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            desktop.Object,
            Mock.Of<IMessenger>(),
            TimeProvider.System)
        {
            Name = "Incomplete task",
        };

        await viewModel.SaveCommand.ExecuteAsync(null);

        viewModel.ValidationText.Should().NotBeEmpty();
        viewModel.Name.Should().Be("Incomplete task");
        viewModel.IsDirty.Should().BeTrue();
        desktop.Verify();
    }

    /// <summary>
    /// Verifies Back does not discard a changed task definition without confirmation.
    /// </summary>
    [Fact]
    public void BackKeepsChangedDraftWhenDiscardIsCanceled()
    {
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(
                service => service.Confirm(
                    "Discard unsaved task changes",
                    "Discard the unsaved task definition and return to Tasks?"))
            .Returns(false);
        var messenger = new StrongReferenceMessenger();
        var recipient = new object();
        var navigatedToTasks = false;
        messenger.Register<NavigateMessage>(
            recipient,
            (_, message) => navigatedToTasks = message.Route == PageRoute.Tasks);
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            desktop.Object,
            messenger,
            TimeProvider.System)
        {
            Name = "Unsaved draft",
        };

        viewModel.BackToTasksCommand.Execute(null);

        navigatedToTasks.Should().BeFalse();
        viewModel.Name.Should().Be("Unsaved draft");
        desktop.VerifyAll();
    }

    /// <summary>
    /// Verifies helper Apply and one-level Undo atomically cover workflow, tools, and grants.
    /// </summary>
    [Fact]
    public void AppliedAIChangesCanBeUndoneAsOneEditorOperation()
    {
        var path = ScheduledTaskValidator.NormalizeAccessPath(
            Path.Combine(Path.GetTempPath(), "OD.Stage13.Editor"));
        var dialog = new Mock<IAIWorkflowHelperDialogService>(MockBehavior.Strict);
        dialog.Setup(
                instance => instance.Show(
                    It.Is<AIWorkflowHelperRequest>(
                        request => request.Mode == AIWorkflowHelperMode.Author)))
            .Returns(
                new AIWorkflowHelperResult(
                    """[{"step":"filesystem.list","args":{"path":"."}}]""",
                    [ToolGroups.FileSystem],
                    [
                        new FileSystemAccessRule
                        {
                            Path = path,
                            Access = FileSystemAccessMode.ReadOnly,
                        },
                    ]));
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System,
            dialog.Object)
        {
            WorkflowText = "[]",
        };

        viewModel.OpenAIHelperCommand.Execute(null);

        viewModel.WorkflowText.Should().Be(
            """[{"step":"filesystem.list","args":{"path":"."}}]""");
        viewModel.UseFileSystem.Should().BeTrue();
        viewModel.AuthorizedLocations.Should().ContainSingle();
        viewModel.HasAIUndoSnapshot.Should().BeTrue();

        viewModel.UndoAIChangesCommand.Execute(null);

        viewModel.WorkflowText.Should().Be("[]");
        viewModel.UseFileSystem.Should().BeFalse();
        viewModel.AuthorizedLocations.Should().BeEmpty();
        viewModel.HasAIUndoSnapshot.Should().BeFalse();
        dialog.VerifyAll();
    }

    /// <summary>
    /// Verifies independent override controls update provenance, dirty state, validation, and persistence.
    /// </summary>
    [Fact]
    public async Task GenerationOverrideControlsPersistPartialValues()
    {
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    "generation-editor",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledTask?)null);
        ScheduledTask? saved = null;
        store.Setup(
                instance => instance.SaveAsync(
                    It.IsAny<ScheduledTask>(),
                    It.IsAny<CancellationToken>()))
            .Callback<ScheduledTask, CancellationToken>(
                (task, _) => saved = task)
            .Returns(Task.CompletedTask);
        var scheduler = new Mock<ITaskSchedulerService>(MockBehavior.Strict);
        scheduler.Setup(
                instance => instance.RegisterAsync(
                    It.IsAny<ScheduledTask>(),
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var ai = new MutableOptionsMonitor<AIOptions>(
            new AIOptions
            {
                GenerationDefaults = new AIGenerationDefaults
                {
                    Temperature = 0.2,
                    TopP = 0.9,
                    ContextWindowTokens = 8192,
                },
            });
        var viewModel = new TaskEditorViewModel(
            store.Object,
            scheduler.Object,
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System,
            workflowHelperDialog: null,
            safetyOptions: null,
            aiOptions: ai)
        {
            Id = "generation-editor",
            Name = "Generation editor",
            Prompt = "Test",
            UseGlobalTemperature = false,
            TemperatureOverrideText = "0.45",
            UseGlobalTopP = true,
        };

        viewModel.IsDirty.Should().BeTrue();
        viewModel.HasGenerationValidationError.Should().BeFalse();
        viewModel.EffectiveTemperatureText.Should().Contain("Task");
        viewModel.EffectiveTopPText.Should().Contain("Settings");
        viewModel.SaveCommand.CanExecute(null).Should().BeTrue();

        await viewModel.SaveCommand.ExecuteAsync(null);

        saved.Should().NotBeNull();
        saved!.GenerationOptions.Should().NotBeNull();
        saved.GenerationOptions!.Temperature.Should().Be(0.45);
        saved.GenerationOptions.TopP.Should().BeNull();
        viewModel.IsDirty.Should().BeFalse();
        store.VerifyAll();
        scheduler.VerifyAll();
    }

    /// <summary>
    /// Verifies opening Prompt editor carries the current unsaved inherit/override snapshot.
    /// </summary>
    [Fact]
    public async Task OpenPromptCarriesUnsavedGenerationOverride()
    {
        var storedTask = new ScheduledTask
        {
            Id = "prompt-snapshot",
            Name = "Prompt snapshot",
            Schedule = "0 7 * * *",
            Prompt = "Test",
            Tools = [],
            FileSystemAccess = [],
            Workflow = [],
            Enabled = true,
            ErrorPolicy = WorkflowErrorPolicy.StopOnError,
            GenerationOptions = new GenerationOptions
            {
                Temperature = 0.8,
                TopP = 0.7,
            },
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    storedTask.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedTask);
        var messenger = new StrongReferenceMessenger();
        var recipient = new object();
        EditPromptMessage? received = null;
        messenger.Register<EditPromptMessage>(
            recipient,
            (_, message) => received = message);
        var viewModel = new TaskEditorViewModel(
            store.Object,
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            messenger,
            TimeProvider.System);

        viewModel.Open(storedTask.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.UseGlobalTemperature = false;
        viewModel.TemperatureOverrideText = "0.35";
        viewModel.UseGlobalTopP = true;

        viewModel.OpenPromptCommand.Execute(null);

        received.Should().NotBeNull();
        received!.TaskId.Should().Be(storedTask.Id);
        received.UseGenerationOptionsSnapshot.Should().BeTrue();
        received.GenerationOptions.Should().NotBeNull();
        received.GenerationOptions!.Temperature.Should().Be(0.35);
        received.GenerationOptions.TopP.Should().BeNull();
        store.VerifyAll();
    }

}
