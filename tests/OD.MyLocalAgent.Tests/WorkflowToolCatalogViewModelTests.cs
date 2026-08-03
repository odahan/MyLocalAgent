using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the deterministic Tool catalog session rules of Stage 17 V2.
/// </summary>
public sealed class WorkflowToolCatalogViewModelTests
{
    /// <summary>
    /// Verifies catalog grouping follows the shared group metadata rather than a UI-local list.
    /// </summary>
    [Fact]
    public void ItemsFollowSharedToolGroupDisplayOrder()
    {
        var viewModel = Create();

        var actualGroups = viewModel.Items
            .Select(static item => item.ToolGroup)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var expectedGroups = ToolGroups.Catalog
            .Where(group => ToolMap.Definitions.Any(
                tool => string.Equals(
                    tool.ToolGroup,
                    group.Name,
                    StringComparison.OrdinalIgnoreCase)))
            .Select(static group => group.Name);

        actualGroups.Should().Equal(expectedGroups);
    }

    /// <summary>
    /// Verifies a session without any insertion returns no result at all.
    /// </summary>
    [Fact]
    public void CloseWithoutChangeReturnsNull()
    {
        var viewModel = Create();

        viewModel.CloseCommand.Execute(null);

        viewModel.Result.Should().BeNull();
    }

    /// <summary>
    /// Verifies repeated insertions accumulate in the working copy without closing.
    /// </summary>
    [Fact]
    public void RepeatedInsertAccumulatesDeterministically()
    {
        var viewModel = Create();
        viewModel.SelectedItem = Select(viewModel, ToolMap.PromptAlias);

        viewModel.InsertCommand.Execute(null);
        viewModel.InsertCommand.Execute(null);
        viewModel.CloseCommand.Execute(null);

        var result = viewModel.Result.Should().NotBeNull().And.Subject.As<WorkflowToolCatalogResult>();
        result.Insertions.Should().HaveCount(2);
        result.Insertions.Select(insertion => insertion.StepId)
            .Should().OnlyHaveUniqueItems();
        WorkflowDefinitionSerializer.Deserialize(result.WorkflowText)
            .Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies cancelling discards the complete working copy.
    /// </summary>
    [Fact]
    public void CancelDiscardsTheCompleteSession()
    {
        var viewModel = Create();
        viewModel.SelectedItem = Select(viewModel, ToolMap.PromptAlias);
        viewModel.InsertCommand.Execute(null);

        viewModel.CancelCommand.Execute(null);

        viewModel.Result.Should().BeNull();
    }

    /// <summary>
    /// Verifies built-in AI entries never propose a task tool group.
    /// </summary>
    [Fact]
    public void BuiltInAiNeverProposesAToolGroup()
    {
        var viewModel = Create();
        viewModel.SelectedItem = Select(viewModel, ToolMap.PromptAlias);
        viewModel.EnableMissingToolGroup = true;

        viewModel.InsertCommand.Execute(null);
        viewModel.CloseCommand.Execute(null);

        viewModel.Result!.AcceptedToolGroups.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a non-AI group enters the result only after an explicit opt-in.
    /// </summary>
    [Fact]
    public void MissingNonAiGroupRequiresExplicitOptIn()
    {
        var withoutOptIn = Create();
        withoutOptIn.SelectedItem = Select(withoutOptIn, "web.search");
        withoutOptIn.InsertCommand.Execute(null);
        withoutOptIn.CloseCommand.Execute(null);
        withoutOptIn.Result!.AcceptedToolGroups.Should().BeEmpty();

        var withOptIn = Create();
        withOptIn.SelectedItem = Select(withOptIn, "web.search");
        withOptIn.EnableMissingToolGroup = true;
        withOptIn.InsertCommand.Execute(null);
        withOptIn.CloseCommand.Execute(null);
        withOptIn.Result!.AcceptedToolGroups.Should().Equal(ToolGroups.Web);
    }

    /// <summary>
    /// Verifies invalid JSON receives the verbatim fallback and its exact warning.
    /// </summary>
    [Fact]
    public void InvalidJsonProducesTheFallbackWarning()
    {
        var viewModel = Create("[ { broken");
        viewModel.SelectedItem = Select(viewModel, ToolMap.PromptAlias);

        viewModel.InsertCommand.Execute(null);
        viewModel.CloseCommand.Execute(null);

        var result = viewModel.Result!;
        result.Insertions.Should().ContainSingle().Which.UsedFallback.Should().BeTrue();
        result.Warnings.Should().Equal(WorkflowCaretInsertionService.FallbackWarning);
    }

    /// <summary>
    /// Verifies the accumulated-context option applies only to the prompt alias.
    /// </summary>
    [Fact]
    public void AccumulatedContextAppliesOnlyToThePromptAlias()
    {
        var viewModel = Create();

        viewModel.SelectedItem = Select(viewModel, ToolMap.PromptAlias);
        viewModel.IsAccumulatedContextEnabled.Should().BeTrue();

        viewModel.SelectedItem = Select(viewModel, "web.search");
        viewModel.IsAccumulatedContextEnabled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the search filters over alias, group, description, and argument names.
    /// </summary>
    [Fact]
    public void SearchMatchesAliasGroupDescriptionAndArguments()
    {
        var item = Create().Items.Single(
            entry => string.Equals(entry.Alias, "web.search", StringComparison.Ordinal));

        item.Matches("WEB.SEARCH").Should().BeTrue();
        item.Matches("web").Should().BeTrue();
        item.Matches("maxResults").Should().BeTrue();
        item.Matches("no-such-token").Should().BeFalse();
        item.Matches(null).Should().BeTrue();
    }

    /// <summary>
    /// Verifies insertion is offered on the Steps tab only, because the Placeholders tab exposes
    /// explicit clipboard copy instead of workflow insertion.
    /// </summary>
    [Fact]
    public void InsertionIsOfferedOnTheStepsTabOnly()
    {
        var viewModel = Create();

        viewModel.SelectedTabIndex.Should().Be(0);
        viewModel.IsStepsTabSelected.Should().BeTrue();

        viewModel.SelectedTabIndex = 1;
        viewModel.IsStepsTabSelected.Should().BeFalse();
        viewModel.StatusText.Should().Contain("clipboard");

        viewModel.SelectedTabIndex = 0;
        viewModel.IsStepsTabSelected.Should().BeTrue();
    }

    private static WorkflowToolCatalogItem Select(
        WorkflowToolCatalogViewModel viewModel,
        string alias) =>
        viewModel.Items.Single(
            item => string.Equals(item.Alias, alias, StringComparison.OrdinalIgnoreCase));

    private static WorkflowToolCatalogViewModel Create(string workflowText = "[]")
    {
        var viewModel = new WorkflowToolCatalogViewModel(new StubDesktopInteractionService());
        viewModel.Initialize(
            new WorkflowToolCatalogRequest(
                workflowText,
                workflowText.Length,
                [],
                [],
                new SafetyOptions(),
                WorkflowErrorPolicy.StopOnError,
                0.2,
                1.0));
        return viewModel;
    }

    private sealed class StubDesktopInteractionService : IDesktopInteractionService
    {
        public UnsavedChangesChoice PromptUnsavedChanges(
            string title,
            string message) =>
            UnsavedChangesChoice.Stay;

        public bool Confirm(string title, string message) => true;

        public void ShowError(string title, string message)
        {
        }

        public string? SelectSavePath(
            string title,
            string filter,
            string defaultExtension,
            string suggestedFileName) => null;

        public string? SelectFolder(string title) => null;

        public void CopyText(string text)
        {
        }

        public string CreateDesktopShortcut(string shortcutName, string executablePath) =>
            string.Empty;

        public void OpenFolder(string path)
        {
        }

        public void OpenUri(string uri)
        {
        }
    }
}
