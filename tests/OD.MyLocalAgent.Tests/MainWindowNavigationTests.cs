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
    /// <summary>Verifies Knowledge appears between Tasks and Direct Chat.</summary>
    [Fact]
    public void KnowledgeAndDirectChatNavigationImmediatelyFollowTasks()
    {
        var viewModel = new MainWindowViewModel(
            static _ => new object(),
            new WeakReferenceMessenger(),
            Mock.Of<ISnackbarMessageQueue>());

        var tasksIndex = viewModel.NavigationItems
            .Select((item, index) => (item, index))
            .Single(pair => pair.item.Route == PageRoute.Tasks)
            .index;

        viewModel.NavigationItems[tasksIndex + 1].Route
            .Should().Be(PageRoute.Knowledge);
        viewModel.NavigationItems[tasksIndex + 1].Title
            .Should().Be("Knowledge");
        viewModel.NavigationItems[tasksIndex + 2].Route
            .Should().Be(PageRoute.DirectChat);
    }

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

    /// <summary>
    /// Verifies that a page guard can keep the current page active.
    /// </summary>
    [Fact]
    public async Task NavigationGuardCanCancelNavigation()
    {
        var guardedPage = new GuardedPage(canNavigateAway: false);
        var pages = Enum.GetValues<PageRoute>()
            .ToDictionary(static route => route, static _ => new object());
        var viewModel = new MainWindowViewModel(
            route => pages[route],
            new WeakReferenceMessenger(),
            Mock.Of<ISnackbarMessageQueue>())
        {
            CurrentPage = guardedPage,
        };

        var navigated = await viewModel.NavigateToAsync(PageRoute.Logs);

        navigated.Should().BeFalse();
        viewModel.CurrentPage.Should().BeSameAs(guardedPage);
    }

    /// <summary>
    /// Verifies that window closing uses the same navigation guard.
    /// </summary>
    [Fact]
    public async Task WindowCloseUsesCurrentPageNavigationGuard()
    {
        var guardedPage = new GuardedPage(canNavigateAway: false);
        var viewModel = new MainWindowViewModel(
            static _ => new object(),
            new WeakReferenceMessenger(),
            Mock.Of<ISnackbarMessageQueue>())
        {
            CurrentPage = guardedPage,
        };

        var canClose = await viewModel.CanCloseAsync();

        canClose.Should().BeFalse();
        guardedPage.CallCount.Should().Be(1);
    }

    private sealed class GuardedPage(bool canNavigateAway) : IAsyncNavigationGuard
    {
        public int CallCount { get; private set; }

        public Task<bool> CanNavigateAwayAsync()
        {
            CallCount++;
            return Task.FromResult(canNavigateAway);
        }
    }
}
