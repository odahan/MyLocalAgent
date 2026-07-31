using FluentAssertions;
using CommunityToolkit.Mvvm.Messaging;
using MaterialDesignThemes.Wpf;
using Moq;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies shell navigation between list and editor surfaces.
/// </summary>
public sealed class MainWindowNavigationTests
{
    /// <summary>
    /// Verifies that navigating to Tasks reloads the Tasks page even when its rail item
    /// remained selected while the task editor was displayed.
    /// </summary>
    [Fact]
    public void TasksNavigationLeavesEditorWhenTasksRailItemIsAlreadySelected()
    {
        var pages = Enum.GetValues<PageRoute>()
            .ToDictionary(static route => route, static _ => new object());
        var viewModel = new MainWindowViewModel(
            route => pages[route],
            new WeakReferenceMessenger(),
            Mock.Of<ISnackbarMessageQueue>());
        var tasksItem = viewModel.NavigationItems.Single(
            static item => item.Route == PageRoute.Tasks);
        viewModel.SelectedNavigationItem = tasksItem;
        viewModel.CurrentPage = pages[PageRoute.TaskEditor];

        viewModel.Receive(new NavigateMessage(PageRoute.Tasks));

        viewModel.CurrentPage.Should().BeSameAs(pages[PageRoute.Tasks]);
        viewModel.SelectedNavigationItem.Should().BeSameAs(tasksItem);
    }
}
