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

    private static TasksViewModel CreateViewModel() =>
        new(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IRunnerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            new WeakReferenceMessenger());
}
