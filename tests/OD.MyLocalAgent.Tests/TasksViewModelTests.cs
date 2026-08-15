using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies session-only Tasks Runner display options.
/// </summary>
public sealed class TasksViewModelTests
{
    /// <summary>
    /// Verifies Large can be expanded and collapsed repeatedly and auto-scroll starts enabled.
    /// </summary>
    [Fact]
    public void RunnerDisplayOptionsRemainRepeatable()
    {
        var viewModel = CreateViewModel();
        var largeNotifications = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TasksViewModel.IsLargeRunnerOutput))
            {
                largeNotifications++;
            }
        };

        viewModel.IsLargeRunnerOutput.Should().BeFalse();
        viewModel.IsRunnerAutoScrollEnabled.Should().BeTrue();

        viewModel.IsLargeRunnerOutput = true;
        viewModel.IsLargeRunnerOutput.Should().BeTrue();
        viewModel.IsLargeRunnerOutput = false;
        viewModel.IsLargeRunnerOutput.Should().BeFalse();
        viewModel.IsLargeRunnerOutput = true;
        viewModel.IsLargeRunnerOutput.Should().BeTrue();
        viewModel.IsLargeRunnerOutput = false;
        viewModel.IsLargeRunnerOutput.Should().BeFalse();

        largeNotifications.Should().Be(4);
        viewModel.IsRunnerAutoScrollEnabled = false;
        viewModel.IsRunnerAutoScrollEnabled.Should().BeFalse();
        viewModel.IsRunnerAutoScrollEnabled = true;
        viewModel.IsRunnerAutoScrollEnabled.Should().BeTrue();
    }

    /// <summary>
    /// Verifies runner detection failures surface in the status bar without
    /// polluting the Runner output panel.
    /// </summary>
    [Fact]
    public async Task RunNowKeepsRunnerOutputIntactWhenRunnerIsNotDetected()
    {
        var runnerService = new Mock<IRunnerService>();
        runnerService
            .SetupGet(service => service.RunnerPath)
            .Throws(new FileNotFoundException("Runner executable was not found."));
        var incidentReporter = new Mock<IIncidentReporter>();
        incidentReporter
            .Setup(reporter => reporter.ReportAsync(
                It.IsAny<Exception>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("correlation-id");
        var viewModel = new TasksViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            runnerService.Object,
            Mock.Of<IRunLogCatalog>(),
            incidentReporter.Object,
            Mock.Of<IDesktopInteractionService>(),
            new WeakReferenceMessenger())
        {
            SelectedTask = new TaskListItem(
                "demo-task",
                "Demo task",
                "* * * * *",
                "n/a",
                "n/a",
                "n/a",
                IsEnabled: true,
                IsRegistered: false),
        };
        var initialOutput = viewModel.RunnerOutput;

        await viewModel.RunNowCommand.ExecuteAsync(null);

        viewModel.RunnerOutput.Should().Be(initialOutput);
        viewModel.StatusText.Should().Be("Runner executable was not found.");
        viewModel.IsRunning.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the task grid uses the unified runner logs for its latest-run
    /// time and status instead of the legacy workflow report index.
    /// </summary>
    [Fact]
    public async Task RefreshUsesLatestUnifiedRunLogForTaskHistory()
    {
        var taskStore = new Mock<ITaskStore>();
        taskStore
            .Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ScheduledTask
                {
                    Id = "demo-task",
                    Name = "Demo task",
                    Schedule = "0 9 * * *",
                    Enabled = false,
                },
            ]);
        var latestStarted = new DateTimeOffset(
            2026,
            8,
            3,
            10,
            30,
            0,
            TimeSpan.Zero);
        var runLogCatalog = new Mock<IRunLogCatalog>();
        runLogCatalog
            .Setup(static catalog => catalog.GetRuns())
            .Returns(
            [
                new RunLogRunInfo(
                    @"C:\logs\demo-task\latest.jsonl",
                    "demo-task",
                    latestStarted,
                    latestStarted.AddMinutes(2),
                    5),
                new RunLogRunInfo(
                    @"C:\logs\demo-task\older.jsonl",
                    "demo-task",
                    latestStarted.AddDays(-1),
                    latestStarted.AddDays(-1).AddMinutes(1),
                    0),
            ]);
        var viewModel = new TasksViewModel(
            taskStore.Object,
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IRunnerService>(),
            runLogCatalog.Object,
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            new WeakReferenceMessenger());

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.Tasks.Should().ContainSingle();
        viewModel.Tasks[0].LastRun.Should().NotBe("Never");
        viewModel.Tasks[0].LastStatus.Should().Be("Failed (5)");
    }

    /// <summary>
    /// Verifies enabled tasks are shown first, with each group sorted by name.
    /// </summary>
    [Fact]
    public async Task RefreshPlacesDisabledTasksAfterEnabledTasks()
    {
        var taskStore = new Mock<ITaskStore>();
        taskStore
            .Setup(store => store.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ScheduledTask { Id = "disabled-a", Name = "Alpha disabled", Schedule = "0 9 * * *", Enabled = false },
                new ScheduledTask { Id = "enabled-z", Name = "Zulu enabled", Schedule = "0 9 * * *", Enabled = true },
                new ScheduledTask { Id = "disabled-b", Name = "Bravo disabled", Schedule = "0 9 * * *", Enabled = false },
                new ScheduledTask { Id = "enabled-a", Name = "Alpha enabled", Schedule = "0 9 * * *", Enabled = true },
            ]);
        var scheduler = new Mock<ITaskSchedulerService>();
        scheduler
            .Setup(service => service.GetNextRunAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);
        var runLogCatalog = new Mock<IRunLogCatalog>();
        runLogCatalog
            .Setup(static catalog => catalog.GetRuns())
            .Returns([]);
        var viewModel = new TasksViewModel(
            taskStore.Object,
            scheduler.Object,
            Mock.Of<IRunnerService>(),
            runLogCatalog.Object,
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            new WeakReferenceMessenger());

        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.Tasks.Select(static task => task.Name).Should().ContainInOrder(
            "Alpha enabled",
            "Zulu enabled",
            "Alpha disabled",
            "Bravo disabled");
    }

    /// <summary>
    /// Verifies Open folder opens the canonical task-store directory.
    /// </summary>
    [Fact]
    public void OpenFolderUsesCanonicalTasksDirectory()
    {
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop
            .Setup(service => service.OpenFolder(LocalAppPaths.TasksDirectory));
        var viewModel = new TasksViewModel(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IRunnerService>(),
            Mock.Of<IRunLogCatalog>(),
            Mock.Of<IIncidentReporter>(),
            desktop.Object,
            new WeakReferenceMessenger());

        viewModel.OpenTasksFolderCommand.Execute(null);

        desktop.VerifyAll();
    }

    private static TasksViewModel CreateViewModel() =>
        new(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IRunnerService>(),
            Mock.Of<IRunLogCatalog>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            new WeakReferenceMessenger());
}
