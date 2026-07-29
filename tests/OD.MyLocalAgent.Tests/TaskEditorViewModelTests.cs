using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
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
}
