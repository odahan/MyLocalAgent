using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the extensible tool-group catalog and its metadata-driven UI projection.
/// </summary>
public sealed class ToolGroupCatalogTests
{
    /// <summary>
    /// Verifies the catalog is ordered, unique, and consistent with the persisted whitelist.
    /// </summary>
    [Fact]
    public void CatalogAndWhitelistAreConsistent()
    {
        ToolGroups.Catalog.Select(static group => group.Name)
            .Should().OnlyHaveUniqueItems();
        ToolGroups.Catalog.Should().OnlyContain(
            static group =>
                !string.IsNullOrWhiteSpace(group.Name) &&
                !string.IsNullOrWhiteSpace(group.DisplayName) &&
                !string.IsNullOrWhiteSpace(group.Description));

        var whitelistNames = ToolGroups.TaskWhitelist
            .Select(static group => group.Name)
            .ToArray();
        whitelistNames.Should().Equal(
            ToolGroups.Catalog
                .Where(static group => group.IsTaskWhitelistGroup)
                .Select(static group => group.Name));
        ToolGroups.All.Should().BeEquivalentTo(whitelistNames);
        ToolGroups.TaskWhitelist.Should().OnlyContain(
            static group => group.IsTaskWhitelistGroup);
        ToolGroups.Find(ToolGroups.FileSystem).Should().BeSameAs(
            ToolGroups.TaskWhitelist.Single(
                static group => group.Name == ToolGroups.FileSystem));
        ToolGroups.GetDisplayOrder(ToolGroups.Gmail).Should().BeLessThan(
            ToolGroups.GetDisplayOrder(ToolGroups.Ai));
    }

    /// <summary>
    /// Verifies every Tool Map entry uses catalog metadata and every task group owns a tool.
    /// </summary>
    [Fact]
    public void ToolMapAndGroupCatalogAreBidirectionallyComplete()
    {
        ToolMap.Definitions.Should().OnlyContain(
            definition => ToolGroups.Find(definition.ToolGroup) != null);

        foreach (var group in ToolGroups.TaskWhitelist)
        {
            ToolMap.Definitions.Should().Contain(
                definition =>
                    !definition.IsAiStep &&
                    string.Equals(
                        definition.ToolGroup,
                        group.Name,
                        StringComparison.OrdinalIgnoreCase),
                $"task group '{group.Name}' must expose at least one runtime tool");
        }

        ToolMap.Definitions
            .Where(static definition => definition.IsAiStep)
            .Should().OnlyContain(
                static definition => definition.ToolGroup == ToolGroups.Ai);
    }

    /// <summary>
    /// Verifies the task editor derives labels, order, and local-path relevance from metadata.
    /// </summary>
    [Fact]
    public void TaskEditorProjectsEveryWhitelistGroupFromCatalogMetadata()
    {
        var viewModel = CreateTaskEditor();

        viewModel.ToolGroupSelections.Select(static item => item.Name).Should().Equal(
            ToolGroups.TaskWhitelist.Select(static group => group.Name));
        viewModel.ToolGroupSelections.Select(static item => item.DisplayName).Should().Equal(
            ToolGroups.TaskWhitelist.Select(static group => group.DisplayName));
        viewModel.ToolGroupSelections.Should().OnlyContain(
            static item => !string.IsNullOrWhiteSpace(item.Description));
        viewModel.ToolGroupSelections.Should().OnlyContain(
            static item => !item.IsSelected);
        viewModel.ToolGroupSelections.Single(
                static item => item.Name == ToolGroups.FileSystem)
            .RequiresLocalPathAuthorization.Should().BeTrue();
        viewModel.ToolGroupSelections.Single(
                static item => item.Name == ToolGroups.Dropbox)
            .RequiresLocalPathAuthorization.Should().BeTrue();
        viewModel.ToolGroupSelections.Single(
                static item => item.Name == ToolGroups.Gmail)
            .RequiresLocalPathAuthorization.Should().BeFalse();

        viewModel.ShowAuthorizedLocations.Should().BeFalse();
        viewModel.SetToolGroupSelected(ToolGroups.Dropbox, isSelected: true);
        viewModel.ShowAuthorizedLocations.Should().BeTrue();
        viewModel.IsDirty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the tester uses the same stable group order as the shared catalog.
    /// </summary>
    [Fact]
    public void ToolTesterUsesSharedCatalogOrder()
    {
        var registry = new Mock<IToolRegistry>();
        registry.SetupGet(static instance => instance.Definitions)
            .Returns(ToolMap.Definitions);
        var viewModel = new ToolTesterViewModel(
            registry.Object,
            Mock.Of<IIncidentReporter>());

        var expectedGroups = ToolGroups.TaskWhitelist
            .Where(
                static group => ToolMap.Definitions.Any(
                    definition =>
                        definition.IsAgentTool &&
                        string.Equals(
                            definition.ToolGroup,
                            group.Name,
                            StringComparison.OrdinalIgnoreCase)))
            .Select(static group => group.Name);

        viewModel.Aliases
            .Select(static alias => alias.Group)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().Equal(expectedGroups);
        var aliases = viewModel.Aliases.Select(static alias => alias.Alias).ToArray();
        aliases.Should().Contain("filesystem.moveToBin");
        aliases.Should().Contain("filesystem.markdownToHtml");
        aliases.Should().Contain("filesystem.htmlToText");
    }

    /// <summary>
    /// Verifies unknown and authoring-only groups cannot be selected as task permissions.
    /// </summary>
    [Theory]
    [InlineData("future")]
    [InlineData(ToolGroups.Ai)]
    public void TaskEditorRejectsNonWhitelistGroupSelection(string group)
    {
        var viewModel = CreateTaskEditor();

        var setAction = () => viewModel.SetToolGroupSelected(group, isSelected: true);
        var readAction = () => viewModel.IsToolGroupSelected(group);

        setAction.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown task tool group*");
        readAction.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown task tool group*");
    }

    private static TaskEditorViewModel CreateTaskEditor() =>
        new(
            Mock.Of<ITaskStore>(),
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System);
}
