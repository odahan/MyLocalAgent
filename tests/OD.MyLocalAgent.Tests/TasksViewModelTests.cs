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

    private static TasksViewModel CreateViewModel() =>
        new(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IRunnerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            new WeakReferenceMessenger());
}
