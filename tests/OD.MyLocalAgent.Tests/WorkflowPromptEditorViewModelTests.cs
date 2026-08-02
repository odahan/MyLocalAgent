using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the deterministic Workflow prompt editor session rules of Stage 17 V2.
/// </summary>
public sealed class WorkflowPromptEditorViewModelTests
{
    private const string Workflow =
        """
        [
          { "step": "ai.prompt", "id": "first", "args": { "prompt": "Summarize.", "includeContext": false } },
          { "step": "fs.read", "args": { "path": "C:/in.txt" } },
          { "step": "ai.prompt", "args": { "prompt": "Translate.", "includeContext": true } }
        ]
        """;

    /// <summary>
    /// Verifies only <c>ai.prompt</c> steps are extracted, in declaration order.
    /// </summary>
    [Fact]
    public void OnlyPromptStepsAreExtractedInDeclarationOrder()
    {
        var viewModel = Create();

        viewModel.Items.Select(item => item.Position).Should().Equal(0, 2);
        viewModel.Items.Select(item => item.DisplayPosition).Should().Equal(1, 3);
    }

    /// <summary>
    /// Verifies Apply is blocked until an effective change exists.
    /// </summary>
    [Fact]
    public void ApplyRequiresAnEffectiveChange()
    {
        var viewModel = Create();

        viewModel.CanApply().Should().BeFalse();

        viewModel.Items[0].PromptText = "Summarize the message.";

        viewModel.CanApply().Should().BeTrue();
    }

    /// <summary>
    /// Verifies a blank prompt blocks Apply for the whole session.
    /// </summary>
    [Fact]
    public void BlankPromptBlocksApply()
    {
        var viewModel = Create();

        viewModel.Items[0].PromptText = "   ";

        viewModel.HasChanges.Should().BeTrue();
        viewModel.IsValid.Should().BeFalse();
        viewModel.CanApply().Should().BeFalse();
    }

    /// <summary>
    /// Verifies Apply writes every pending edit once and preserves unrelated steps.
    /// </summary>
    [Fact]
    public void ApplyWritesEveryPendingEdit()
    {
        var viewModel = Create();
        viewModel.Items[0].PromptText = "Summarize the message.";
        viewModel.Items[1].IncludeContext = false;

        viewModel.ApplyCommand.Execute(null);

        var result = viewModel.Result.Should().NotBeNull().And
            .Subject.As<WorkflowPromptEditorResult>();
        result.ModifiedPromptCount.Should().Be(1);
        result.ModifiedContextCount.Should().Be(1);
        result.ModifiedSteps.Select(step => step.Position).Should().Equal(0, 2);

        var workflow = WorkflowDefinitionSerializer.Deserialize(result.WorkflowText);
        workflow.Should().HaveCount(3);
        workflow[1].Step.Should().Be("fs.read");
    }

    /// <summary>
    /// Verifies Revert restores the extracted state of the selected entry only.
    /// </summary>
    [Fact]
    public void RevertRestoresTheSelectedEntryOnly()
    {
        var viewModel = Create();
        viewModel.Items[0].PromptText = "Changed.";
        viewModel.Items[1].PromptText = "Changed too.";
        viewModel.SelectedItem = viewModel.Items[0];

        viewModel.RevertStepCommand.Execute(null);

        viewModel.Items[0].IsModified.Should().BeFalse();
        viewModel.Items[1].IsModified.Should().BeTrue();
    }

    /// <summary>
    /// Verifies Cancel discards the whole session.
    /// </summary>
    [Fact]
    public void CancelDiscardsTheSession()
    {
        var viewModel = Create();
        viewModel.Items[0].PromptText = "Changed.";

        viewModel.CancelCommand.Execute(null);

        viewModel.Result.Should().BeNull();
    }

    /// <summary>
    /// Verifies the duplication warning appears only when context is accumulated.
    /// </summary>
    [Fact]
    public void ContextDuplicationIsWarnedWithoutBlocking()
    {
        var viewModel = Create();
        var item = viewModel.Items[1];

        item.PromptText = "Translate {{workflow.context}}.";

        item.HasContextDuplication.Should().BeTrue();
        viewModel.CanApply().Should().BeTrue();

        item.IncludeContext = false;

        item.HasContextDuplication.Should().BeFalse();
    }

    private static WorkflowPromptEditorViewModel Create()
    {
        var viewModel = new WorkflowPromptEditorViewModel();
        viewModel.Initialize(new WorkflowPromptEditorRequest(Workflow));
        return viewModel;
    }
}
