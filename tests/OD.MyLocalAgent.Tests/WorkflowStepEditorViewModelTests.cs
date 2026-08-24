using FluentAssertions;
using Moq;
using System.Text.Json;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Tests deterministic visibility and scope rules of the workflow step editor form.</summary>
public sealed class WorkflowStepEditorViewModelTests
{
    /// <summary>Verifies prompt-only and AI-only sections follow the selected Tool Map definition.</summary>
    [Fact]
    public void SelectedToolControlsPromptAndGenerationSections()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.IsPromptSelected.Should().BeTrue();
        viewModel.IsAiSelected.Should().BeTrue();
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.read");
        viewModel.IsPromptSelected.Should().BeFalse();
        viewModel.IsAiSelected.Should().BeFalse();
    }

    /// <summary>Verifies Knowledge narrowing appears only for the exact Knowledge search alias.</summary>
    [Fact]
    public void KnowledgeNarrowingRequiresExactKnowledgeSearchAlias()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.ToolAliases = "filesystem.read";
        viewModel.ShowKnowledgeTags.Should().BeFalse();
        viewModel.ToolAliases = "knowledge.search";
        viewModel.ShowKnowledgeTags.Should().BeTrue();
    }

    /// <summary>Verifies missing authorizations are proposals rather than implicit task changes.</summary>
    [Fact]
    public void MissingToolGroupStartsAsUncheckedProposal()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.read");
        viewModel.AuthorizationProposals.Should().ContainSingle(item => item.Kind == "Tool group" && !item.IsAccepted);
    }

    /// <summary>Verifies accepting a proposal counts as entered data when changing tools.</summary>
    [Fact]
    public void ToolChangeKeepsAcceptedProposalWhenDiscardIsCanceled()
    {
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(service => service.Confirm("Discard workflow step fields", It.IsAny<string>())).Returns(false);
        var viewModel = new WorkflowStepEditorViewModel(desktopInteraction: desktop.Object);
        viewModel.Initialize(Request());
        var original = ToolCatalog.Default.Find("filesystem.read")!;
        viewModel.SelectedTool = original;
        viewModel.AuthorizationProposals.Single(item => item.Kind == "Tool group").IsAccepted = true;

        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.write");

        viewModel.SelectedTool.Should().BeSameAs(original);
        viewModel.AuthorizationProposals.Should().Contain(item => item.Kind == "Tool group" && item.IsAccepted);
        desktop.VerifyAll();
    }

    /// <summary>Verifies structural Apply remains possible while missing authorizations stay opt-in warnings.</summary>
    [Fact]
    public void ProvisionalValidationAllowsUncheckedAuthorizationProposal()
    {
        var request = Request() with
        {
            DraftTask = new ScheduledTask
            {
                Id = "stage28-draft",
                Name = "Stage 28 draft",
                Schedule = "0 9 * * *",
                Prompt = "Read a file.",
                Tools = [ToolGroups.Knowledge],
            },
        };
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(request);
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.read");
        viewModel.Arguments.Single(item => item.Definition.Name == "path").Value = ".";

        viewModel.ApplyCommand.Execute(null);

        viewModel.Result.Should().NotBeNull();
        viewModel.Result!.AcceptedToolGroups.Should().BeEmpty();
        viewModel.Result.Warnings.Should().Contain(item => item.Contains("filesystem", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies a literal absolute local path becomes an unchecked proposal.</summary>
    [Fact]
    public void LiteralAbsolutePathStartsAsUncheckedProposal()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.write");
        viewModel.Arguments.Single(item => item.Definition.Name == "path").Value = @"C:\Stage28\report.txt";
        viewModel.AuthorizationProposals.Should().Contain(item => item.Kind == "Path" && !item.IsAccepted);
    }

    /// <summary>Verifies catalog numeric authoring bounds are enforced before Apply.</summary>
    [Fact]
    public void NumericEditorRejectsValueOutsideCatalogBounds()
    {
        var item = new WorkflowStepArgumentEditorItem(
            new ToolArgumentDefinition("count", WorkflowArgumentKind.Integer, true, "Count.") { Minimum = 1, Maximum = 3 }, true, "4");
        var action = () => item.Parse();
        action.Should().Throw<FormatException>().WithMessage("*between 1 and 3*");
    }

    /// <summary>Verifies the insert selector filters only the immutable catalog projection.</summary>
    [Fact]
    public void ToolSearchFiltersCatalogByAliasOrDescription()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.ToolSearchText = "filesystem.write";
        viewModel.FilteredTools.Should().ContainSingle().Which.Alias.Should().Be("filesystem.write");
    }

    /// <summary>Verifies structured arguments reject primitive JSON values.</summary>
    [Fact]
    public void StructuredEditorRejectsPrimitiveJson()
    {
        var item = new WorkflowStepArgumentEditorItem(new ToolArgumentDefinition("options", WorkflowArgumentKind.Object, true, "Options."), true, "42");
        var action = () => item.Parse();
        action.Should().Throw<FormatException>().WithMessage("*object or array*");
    }

    /// <summary>Verifies a shared placeholder is inserted only into a placeholder-enabled argument.</summary>
    [Fact]
    public void PlaceholderInsertionUsesSharedCatalogAndArgumentMetadata()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.SelectedArgument = viewModel.Arguments.Single(item => item.Definition.Name == "prompt");
        viewModel.SelectedArgument.Value = "replace this text";
        viewModel.SelectedArgument.SelectionStart = 8;
        viewModel.SelectedArgument.SelectionLength = 4;
        viewModel.SelectedPlaceholder = WorkflowPlaceholderCatalog.Entries.First();
        viewModel.InsertPlaceholderCommand.Execute(null);
        viewModel.SelectedArgument.Value.Should().Be("replace {{task.id}} text");
    }

    /// <summary>Verifies an otherwise valid prompt tool produces an opt-in group proposal.</summary>
    [Fact]
    public void PromptToolMissingGroupProducesUncheckedProposal()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.ToolAliases = "filesystem.read";
        viewModel.AuthorizationProposals.Should().Contain(item => item.Kind == "Tool group" && item.Key == ToolGroups.FileSystem && !item.IsAccepted);
    }

    /// <summary>Verifies exact prompt aliases serialize in immutable catalog order.</summary>
    [Fact]
    public void ExactPromptAliasesSerializeInCatalogOrder()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.ToolAliases = "knowledge.search, filesystem.read";
        viewModel.ApplyCommand.Execute(null);
        var step = WorkflowDefinitionSerializer.Deserialize(viewModel.Result!.WorkflowText).Single();
        step.ToolAliases.Should().Equal(
            ToolCatalog.Default.Definitions.Where(tool => tool.IsAgentTool && new[] { "knowledge.search", "filesystem.read" }.Contains(tool.Alias)).Select(static tool => tool.Alias));
    }

    /// <summary>Verifies an explicit blank prompt model is rejected while inheritance remains valid.</summary>
    [Fact]
    public void ExplicitBlankPromptModelIsRejected()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.InheritModel = false;
        viewModel.Model = "   ";
        viewModel.ApplyCommand.Execute(null);
        viewModel.Result.Should().BeNull();
        viewModel.StatusText.Should().Contain("cannot be blank");
    }

    /// <summary>Verifies each AI generation property independently preserves inheritance.</summary>
    [Fact]
    public void GenerationOverridesAreIndependent()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.summarize");
        viewModel.OverrideTopP = true;
        viewModel.TopP = "0.75";
        viewModel.ApplyCommand.Execute(null);
        var options = WorkflowDefinitionSerializer.Deserialize(viewModel.Result!.WorkflowText).Single().GenerationOptions!;
        options.TopP.Should().Be(0.75);
        options.Temperature.Should().BeNull();
        options.ReasoningEnabled.Should().BeNull();
    }

    /// <summary>Verifies narrowing Knowledge requires a non-empty task-bounded selection.</summary>
    [Fact]
    public void EmptyKnowledgeNarrowingIsRejected()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.ToolAliases = "knowledge.search";
        viewModel.NarrowKnowledge = true;
        viewModel.ApplyCommand.Execute(null);
        viewModel.Result.Should().BeNull();
        viewModel.StatusText.Should().Contain("at least one");
    }

    /// <summary>Verifies nullable metadata preserves an explicitly included JSON null.</summary>
    [Fact]
    public void NullableArgumentPreservesExplicitNull()
    {
        var item = new WorkflowStepArgumentEditorItem(
            new ToolArgumentDefinition("value", WorkflowArgumentKind.String, false, "Value.") { AllowsNull = true }, true, "null");
        item.Parse().ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Verifies generated editor kinds use metadata rather than UI-local alias tables.</summary>
    [Theory]
    [InlineData(WorkflowArgumentKind.String, false, WorkflowArgumentEditorKind.String)]
    [InlineData(WorkflowArgumentKind.String, true, WorkflowArgumentEditorKind.MultilineString)]
    [InlineData(WorkflowArgumentKind.Integer, false, WorkflowArgumentEditorKind.Integer)]
    [InlineData(WorkflowArgumentKind.Number, false, WorkflowArgumentEditorKind.Number)]
    [InlineData(WorkflowArgumentKind.Boolean, false, WorkflowArgumentEditorKind.Boolean)]
    [InlineData(WorkflowArgumentKind.Enumeration, false, WorkflowArgumentEditorKind.Enumeration)]
    [InlineData(WorkflowArgumentKind.Object, false, WorkflowArgumentEditorKind.StructuredJson)]
    public void EditorKindComesFromCatalogMetadata(WorkflowArgumentKind kind, bool longText, WorkflowArgumentEditorKind expected)
    {
        var item = new WorkflowStepArgumentEditorItem(new ToolArgumentDefinition("value", kind, true, "Value.") { IsLongText = longText }, true, string.Empty);
        item.EditorKind.Should().Be(expected);
    }

    private static WorkflowStepEditorRequest Request() => new(
        "[]", 0, WorkflowStepEditorMode.InsertBefore, [ToolGroups.Knowledge], [], [], ["safe"], WorkflowErrorPolicy.StopOnError, 0.2, 1);
}
