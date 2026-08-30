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
    /// Verifies cloning persists a new identifier, preserves the source, and displays the clone.
    /// </summary>
    [Fact]
    public async Task CloneCreatesAndOpensNewTaskWithoutChangingSource()
    {
        var source = CreatePersistedTask("source-task", "Source task");
        var clone = source with
        {
            Id = "cloned-task",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(instance => instance.GetAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        store.SetupSequence(instance => instance.GetAsync(clone.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledTask?)null)
            .ReturnsAsync(clone);
        store.Setup(instance => instance.SaveAsync(
                It.Is<ScheduledTask>(task => task.Id == clone.Id && task.Name == source.Name),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(clone);
        var scheduler = new Mock<ITaskSchedulerService>(MockBehavior.Strict);
        scheduler.Setup(instance => instance.RegisterAsync(clone, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var cloneDialog = new Mock<ITaskCloneDialogService>(MockBehavior.Strict);
        cloneDialog.Setup(instance => instance.Show(source.Id)).Returns(clone.Id);
        var viewModel = new TaskEditorViewModel(
            store.Object,
            scheduler.Object,
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System,
            cloneDialog: cloneDialog.Object);

        viewModel.Open(source.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.CloneCommand.Execute(null);
        await (viewModel.CloneCommand.ExecutionTask ?? Task.CompletedTask);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.Id.Should().Be(clone.Id);
        viewModel.EditorTitle.Should().Be($"Edit task · {clone.Name}");
        store.VerifyAll();
        scheduler.VerifyAll();
        cloneDialog.VerifyAll();
    }

    /// <summary>
    /// Verifies archiving unregisters the task, delegates durable retention to the store, and returns to the task list.
    /// </summary>
    [Fact]
    public async Task ArchiveUnregistersAndNavigatesBackToTasks()
    {
        var source = CreatePersistedTask("archive-task", "Archive task");
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(instance => instance.GetAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        store.Setup(instance => instance.ArchiveAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync("C:\\archives\\archive-task--archived-20260830T142315.482Z.json");
        var scheduler = new Mock<ITaskSchedulerService>(MockBehavior.Strict);
        scheduler.Setup(instance => instance.UnregisterAsync(source.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(instance => instance.ConfirmDestructive(
                "Archive task",
                It.IsAny<string>(),
                "Archive task"))
            .Returns(true);
        var messenger = new StrongReferenceMessenger();
        var recipient = new object();
        NavigateMessage? navigation = null;
        messenger.Register<NavigateMessage>(recipient, (_, message) => navigation = message);
        var viewModel = new TaskEditorViewModel(
            store.Object,
            scheduler.Object,
            Mock.Of<IIncidentReporter>(),
            desktop.Object,
            messenger,
            TimeProvider.System);

        viewModel.Open(source.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.ArchiveCommand.Execute(null);
        await (viewModel.ArchiveCommand.ExecutionTask ?? Task.CompletedTask);

        navigation.Should().NotBeNull();
        navigation!.Route.Should().Be(PageRoute.Tasks);
        store.VerifyAll();
        scheduler.VerifyAll();
        desktop.VerifyAll();
    }

    /// <summary>
    /// Verifies the combined action saves the clone before unregistering and archiving the source task.
    /// </summary>
    [Fact]
    public async Task CloneAndArchiveCreatesCloneThenArchivesSource()
    {
        var source = CreatePersistedTask("combined-source", "Combined source");
        var clone = source with
        {
            Id = "combined-clone",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };
        var operations = new List<string>();
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(instance => instance.GetAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        store.SetupSequence(instance => instance.GetAsync(clone.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScheduledTask?)null)
            .ReturnsAsync(clone);
        store.Setup(instance => instance.SaveAsync(It.Is<ScheduledTask>(task => task.Id == clone.Id), It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("save-clone"))
            .ReturnsAsync(clone);
        store.Setup(instance => instance.ArchiveAsync(source.Id, It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("archive-source"))
            .ReturnsAsync("C:\\archives\\combined-source--archived-20260830T142315.482Z.json");
        var scheduler = new Mock<ITaskSchedulerService>(MockBehavior.Strict);
        scheduler.Setup(instance => instance.RegisterAsync(clone, It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("register-clone"))
            .Returns(Task.CompletedTask);
        scheduler.Setup(instance => instance.UnregisterAsync(source.Id, It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("unregister-source"))
            .Returns(Task.CompletedTask);
        var cloneDialog = new Mock<ITaskCloneDialogService>(MockBehavior.Strict);
        cloneDialog.Setup(instance => instance.Show(source.Id)).Returns(clone.Id);
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(instance => instance.ConfirmDestructive(
                "Clone and archive task",
                It.IsAny<string>(),
                "Clone and archive"))
            .Returns(true);
        var viewModel = new TaskEditorViewModel(
            store.Object,
            scheduler.Object,
            Mock.Of<IIncidentReporter>(),
            desktop.Object,
            Mock.Of<IMessenger>(),
            TimeProvider.System,
            cloneDialog: cloneDialog.Object);

        viewModel.Open(source.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.CloneAndArchiveCommand.Execute(null);
        await (viewModel.CloneAndArchiveCommand.ExecutionTask ?? Task.CompletedTask);

        operations.Should().ContainInOrder(
            "save-clone",
            "register-clone",
            "unregister-source",
            "archive-source");
        store.VerifyAll();
        scheduler.VerifyAll();
        cloneDialog.VerifyAll();
        desktop.VerifyAll();
    }

    /// <summary>Verifies the step editor receives the model configured for the active provider.</summary>
    [Fact]
    public void StepEditorReceivesConfiguredProviderModel()
    {
        WorkflowStepEditorRequest? captured = null;
        var dialog = new Mock<IWorkflowStepEditorDialogService>(MockBehavior.Strict);
        dialog.Setup(service => service.Show(It.IsAny<WorkflowStepEditorRequest>()))
            .Callback<WorkflowStepEditorRequest>(request => captured = request)
            .Returns((WorkflowStepEditorResult?)null);
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System,
            aiOptions: new MutableOptionsMonitor<AIOptions>(new AIOptions { Provider = AIOptions.OpenAIProvider }),
            stepEditorDialog: dialog.Object,
            ollamaOptions: new MutableOptionsMonitor<OllamaOptions>(new OllamaOptions { Model = "local-model" }),
            openAiOptions: new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions { Model = "remote-model" }));

        viewModel.InsertWorkflowStepBeforeCommand.Execute(null);

        captured.Should().NotBeNull();
        captured!.ConfiguredModel.Should().Be("remote-model");
        dialog.VerifyAll();
    }

    /// <summary>Verifies Knowledge tags come from the library catalog and stale grants remain visible.</summary>
    [Fact]
    public async Task KnowledgeTagCatalogShowsStalePersistedScope()
    {
        var task = new ScheduledTask
        {
            Id = "knowledge-scope",
            Name = "Knowledge scope",
            Schedule = "0 7 * * *",
            Prompt = "Search",
            Tools = [ToolGroups.Knowledge],
            KnowledgeAccess = new KnowledgeAccessDefinition { AllowedTags = ["current", "retired"] },
            Workflow = [],
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(item => item.GetAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        var library = new Mock<IKnowledgeLibraryService>(MockBehavior.Strict);
        library.Setup(item => item.GetTagCatalogAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["current"]);
        var viewModel = new TaskEditorViewModel(
            store.Object,
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System,
            knowledgeLibrary: library.Object);

        viewModel.Open(task.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.KnowledgeTagSelections.Should().HaveCount(2);
        var stale = viewModel.KnowledgeTagSelections.Single(item => item.Name == "retired");
        stale.IsAvailable.Should().BeFalse();
        viewModel.KnowledgeStaleTagWarning.Should().NotBeEmpty();
        stale.IsSelected = false;
        viewModel.KnowledgeTagsText.Should().Be("current");
        viewModel.IsDirty.Should().BeTrue();
        store.VerifyAll();
        library.VerifyAll();
    }

    /// <summary>
    /// Verifies application-owned workspace data cannot be added as a task grant.
    /// </summary>
    [Fact]
    public void ApplicationOwnedLocationCannotBeAuthorized()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Editor",
            Guid.NewGuid().ToString("N"));
        var reserved = Path.Combine(
            workspace,
            WorkspacePaths.ApplicationDirectoryName,
            "tasks");
        Directory.CreateDirectory(reserved);
        try
        {
            var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
            desktop.Setup(instance => instance.SelectFolder(
                    "Select an authorized task location"))
                .Returns(reserved);
            var sandbox = new PathSandbox(
                Microsoft.Extensions.Options.Options.Create(
                    new FileSystemOptions { RootPath = workspace }));
            var viewModel = new TaskEditorViewModel(
                Mock.Of<ITaskStore>(),
                Mock.Of<ITaskSchedulerService>(),
                Mock.Of<IIncidentReporter>(),
                desktop.Object,
                Mock.Of<IMessenger>(),
                TimeProvider.System,
                pathSandbox: sandbox);

            viewModel.AddAuthorizedLocationCommand.Execute(null);

            viewModel.AuthorizedLocations.Should().BeEmpty();
            viewModel.ValidationText.Should().Contain("reserved for application data");
            desktop.VerifyAll();
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

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
                .ReturnsAsync(
                    (ScheduledTask task, CancellationToken _) => task);
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
            };
            viewModel.SetToolGroupSelected(ToolGroups.FileSystem, isSelected: true);
            viewModel.SetToolGroupSelected(ToolGroups.Calendar, isSelected: true);
            viewModel.SetToolGroupSelected(ToolGroups.Web, isSelected: true);

            viewModel.AddAuthorizedLocationCommand.Execute(null);
            viewModel.AuthorizedLocations.Single().CanWrite = false;
            await viewModel.SaveCommand.ExecuteAsync(null);

            viewModel.AuthorizedLocations.Should().ContainSingle();
            savedTask.Should().NotBeNull();
            savedTask!.Tools.Should().Equal(
                ToolGroups.Calendar,
                ToolGroups.FileSystem,
                ToolGroups.Web);
            savedTask.FileSystemAccess.Should().ContainSingle(
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
    /// Verifies task loading projects several saved groups through the generic catalog selection.
    /// </summary>
    [Fact]
    public async Task ExistingTaskLoadsSeveralToolGroupsThroughCatalogSelection()
    {
        var storedTask = new ScheduledTask
        {
            Id = "catalog-selection",
            Name = "Catalog selection",
            Schedule = "0 8 * * *",
            Prompt = "Test",
            Tools = [ToolGroups.Web, ToolGroups.Gmail, ToolGroups.Http],
            FileSystemAccess = [],
            Workflow = [],
            Enabled = true,
            ErrorPolicy = WorkflowErrorPolicy.StopOnError,
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    storedTask.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedTask);
        var viewModel = new TaskEditorViewModel(
            store.Object,
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System);

        viewModel.Open(storedTask.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.ToolGroupSelections
            .Where(static item => item.IsSelected)
            .Select(static item => item.Name)
            .Should().Equal(
                ToolGroups.Gmail,
                ToolGroups.Http,
                ToolGroups.Web);
        viewModel.IsDirty.Should().BeFalse();
        store.VerifyAll();
    }

    /// <summary>
    /// Verifies persisted timestamps are shown as local task details with exact UTC tooltips.
    /// </summary>
    [Fact]
    public async Task ExistingTaskShowsCreationAndModificationDetails()
    {
        var createdUtc = new DateTimeOffset(2026, 8, 19, 8, 15, 0, TimeSpan.Zero);
        var updatedUtc = createdUtc.AddHours(5);
        var storedTask = new ScheduledTask
        {
            Id = "timestamp-details",
            Name = "Timestamp details",
            Schedule = "0 8 * * *",
            Prompt = "Test",
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc,
        };
        var store = new Mock<ITaskStore>(MockBehavior.Strict);
        store.Setup(
                instance => instance.GetAsync(
                    storedTask.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedTask);
        var timeProvider = TimeProvider.System;
        var viewModel = new TaskEditorViewModel(
            store.Object,
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            timeProvider);

        viewModel.Open(storedTask.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.CreatedText.Should().Be(
            TimeZoneInfo.ConvertTime(createdUtc, timeProvider.LocalTimeZone)
                .ToString("g", System.Globalization.CultureInfo.CurrentCulture));
        viewModel.LastModifiedText.Should().Be(
            TimeZoneInfo.ConvertTime(updatedUtc, timeProvider.LocalTimeZone)
                .ToString("g", System.Globalization.CultureInfo.CurrentCulture));
        viewModel.CreatedUtcToolTip.Should().Be(createdUtc.ToString("O"));
        viewModel.LastModifiedUtcToolTip.Should().Be(updatedUtc.ToString("O"));
        store.VerifyAll();
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
    public async Task BackKeepsChangedDraftWhenLeavingIsCanceled()
    {
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(
                service => service.PromptUnsavedChanges(
                    "Unsaved task",
                    "Save your task changes before leaving the editor?"))
            .Returns(UnsavedChangesChoice.Stay);
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

        await viewModel.BackToTasksCommand.ExecuteAsync(null);

        navigatedToTasks.Should().BeFalse();
        viewModel.Name.Should().Be("Unsaved draft");
        desktop.VerifyAll();
    }

    /// <summary>
    /// Verifies abandoning a changed task permits navigation through the shared guard.
    /// </summary>
    [Fact]
    public async Task BackCanDiscardChangedTaskAndNavigate()
    {
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(
                service => service.PromptUnsavedChanges(
                    "Unsaved task",
                    "Save your task changes before leaving the editor?"))
            .Returns(UnsavedChangesChoice.Discard);
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

        await viewModel.BackToTasksCommand.ExecuteAsync(null);

        navigatedToTasks.Should().BeTrue();
        viewModel.IsDirty.Should().BeFalse();
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
        viewModel.IsToolGroupSelected(ToolGroups.FileSystem).Should().BeTrue();
        viewModel.AuthorizedLocations.Should().ContainSingle();
        viewModel.HasAssistedUndoSnapshot.Should().BeTrue();

        viewModel.UndoAssistedChangeCommand.Execute(null);

        viewModel.WorkflowText.Should().Be("[]");
        viewModel.IsToolGroupSelected(ToolGroups.FileSystem).Should().BeFalse();
        viewModel.AuthorizedLocations.Should().BeEmpty();
        viewModel.HasAssistedUndoSnapshot.Should().BeFalse();
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
            .ReturnsAsync(
                (ScheduledTask task, CancellationToken _) => task);
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

    /// <summary>
    /// Verifies workflow validation raises a short snackbar notification in addition to the
    /// detailed status line, so the outcome stays visible when the status line is scrolled out.
    /// </summary>
    [Theory]
    [InlineData("[ { \"step\": \"ai.prompt\", \"id\": \"s1\", \"args\": { \"prompt\": \"x\" } } ]", "Validation OK.")]
    [InlineData("not json", "Invalid workflow.")]
    public void ValidateWorkflowRaisesAShortNotification(string workflowText, string expected)
    {
        var messenger = new StrongReferenceMessenger();
        var recipient = new object();
        var notifications = new List<string>();
        messenger.Register<SnackbarNotificationMessage>(
            recipient,
            (_, message) => notifications.Add(message.Message));
        var viewModel = new TaskEditorViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            messenger,
            TimeProvider.System)
        {
            WorkflowText = workflowText,
        };

        viewModel.ValidateWorkflowCommand.Execute(null);

        notifications.Should().ContainSingle().Which.Should().Be(expected);
        viewModel.ValidationText.Should().NotBeNullOrWhiteSpace();
    }

    private static ScheduledTask CreatePersistedTask(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Schedule = "0 7 * * *",
        Prompt = "Test",
        Tools = [],
        FileSystemAccess = [],
        Workflow = [],
        Enabled = true,
        ErrorPolicy = WorkflowErrorPolicy.StopOnError,
    };
}
