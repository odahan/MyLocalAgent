using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Tests structural, declaration-position workflow editing.</summary>
public sealed class WorkflowStepEditingServiceTests
{
    /// <summary>Verifies the normative caret boundaries for two top-level declarations.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(24, 0)]
    [InlineData(27, 0)]
    [InlineData(29, 1)]
    [InlineData(51, 1)]
    [InlineData(500, 1)]
    public void LocateUsesNormativeTopLevelCaretRules(int caret, int expectedIndex)
    {
        const string text = "[  {\"step\":\"ai.summarize\"},  {\"step\":\"ai.classifyEmail\"}  ]";
        WorkflowStepEditingService.Locate(text, caret).Location!.Index.Should().Be(expectedIndex);
    }

    /// <summary>Verifies an opening token belongs to that declaration rather than the left declaration.</summary>
    [Fact]
    public void LocateSelectsSecondStepAtItsOpeningToken()
    {
        const string text = "[{\"step\":\"ai.summarize\"},{\"step\":\"ai.classifyEmail\"}]";
        var secondOpening = text.IndexOf('{', text.IndexOf(',', StringComparison.Ordinal) + 1);
        WorkflowStepEditingService.Locate(text, secondOpening).Location!.Index.Should().Be(1);
    }

    /// <summary>Verifies invalid roots and trailing JSON are deterministic parse failures.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[] true")]
    [InlineData("[")]
    public void LocateRejectsInvalidWorkflowRoots(string text)
    {
        var result = WorkflowStepEditingService.Locate(text, 0);
        result.Location.Should().BeNull();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Verifies UTF-16 positions remain valid around an emoji surrogate pair.</summary>
    [Fact]
    public void LocateClampsCaretAwayFromSurrogatePair()
    {
        const string text = "[{\"step\":\"ai.prompt\",\"args\":{\"prompt\":\"😀\"}}]";
        var lowSurrogate = text.IndexOf("😀", StringComparison.Ordinal) + 1;
        WorkflowCaretInsertionService.ClampCaret(text, lowSurrogate).Should().Be(lowSurrogate - 1);
        WorkflowStepEditingService.Locate(text, lowSurrogate).Location!.Index.Should().Be(0);
    }
    /// <summary>Verifies a caret on inter-step whitespace selects the declaration on its left.</summary>
    [Fact]
    public void LocateSelectsLeftStepBetweenDeclarations()
    {
        const string text = "[\n  {\"step\":\"ai.prompt\",\"args\":{\"prompt\":\"é😀\"}},\n  \"ai.summarize\"\n]";
        var result = WorkflowStepEditingService.Locate(text, text.IndexOf(',', StringComparison.Ordinal) + 1);
        result.Error.Should().BeNull();
        result.Location!.Index.Should().Be(0);
        result.Location.Alias.Should().Be("ai.prompt");
    }

    /// <summary>Verifies replacement rejects an alias conversion.</summary>
    [Fact]
    public void ReplaceRejectsAliasMutation()
    {
        const string text = "[\"ai.summarize\"]";
        var replacement = new WorkflowStepDefinition { Step = "ai.extractActionItems", Id = "x" };
        var action = () => WorkflowStepEditingService.Replace(text, 0, "ai.summarize", replacement);
        action.Should().Throw<WorkflowValidationException>().WithMessage("*cannot be changed to another tool*");
    }

    /// <summary>Verifies deletion changes only one parsed top-level declaration.</summary>
    [Fact]
    public void DeleteRemovesExactlyOneStep()
    {
        const string text = "[\"ai.summarize\",\"ai.extractActionItems\",\"ai.classifyEmail\"]";
        var result = WorkflowStepEditingService.Delete(text, 1, "ai.extractActionItems");
        WorkflowDefinitionSerializer.Deserialize(result.Text).Select(static item => item.Step).Should().Equal("ai.summarize", "ai.classifyEmail");
        result.AffectedIndex.Should().Be(1);
    }

    /// <summary>Verifies empty arrays accept a deterministic insertion at zero.</summary>
    [Fact]
    public void InsertIntoEmptyArrayUsesIndexZero()
    {
        var result = WorkflowStepEditingService.Insert("[]", null, WorkflowStepInsertionPosition.Before, new WorkflowStepDefinition { Step = "ai.summarize" });
        result.AffectedIndex.Should().Be(0);
        WorkflowDefinitionSerializer.Deserialize(result.Text).Should().ContainSingle().Which.Step.Should().Be("ai.summarize");
    }

    /// <summary>Verifies deleting the only declaration places the caret inside the empty array.</summary>
    [Fact]
    public void DeleteOnlyStepPlacesCaretInsideEmptyArray()
    {
        var result = WorkflowStepEditingService.Delete("[\"ai.summarize\"]", 0, "ai.summarize");
        result.Text.Should().Be("[]");
        result.CaretIndex.Should().Be(1);
    }

    /// <summary>Verifies insert-before and insert-after use declaration positions exactly.</summary>
    [Theory]
    [InlineData(WorkflowStepInsertionPosition.Before, 1)]
    [InlineData(WorkflowStepInsertionPosition.After, 2)]
    public void InsertUsesExactAnchorPosition(WorkflowStepInsertionPosition position, int expectedIndex)
    {
        const string text = "[\"ai.summarize\",\"ai.classifyEmail\"]";
        var result = WorkflowStepEditingService.Insert(text, 1, position, new WorkflowStepDefinition { Step = "ai.extractActionItems", Id = "inserted" });
        result.AffectedIndex.Should().Be(expectedIndex);
        WorkflowDefinitionSerializer.Deserialize(result.Text)[expectedIndex].EffectiveId.Should().Be("inserted");
    }

    /// <summary>Verifies deletion analysis detects only later output placeholders.</summary>
    [Fact]
    public void DependencyAnalysisFindsLaterOutputReferencesOnly()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            "[{'step':'ai.prompt','id':'source','args':{'prompt':'x'}},{'step':'ai.prompt','id':'dependent','args':{'prompt':'{{source.output}}'}},{'step':'ai.prompt','id':'prose','args':{'prompt':'source.output'}}]".Replace('\'', '"'));
        WorkflowStepDependencyAnalyzer.FindLaterReferences(workflow, 0).Should().ContainSingle().Which.StepIndex.Should().Be(1);
    }

    /// <summary>Verifies dependency analysis reports structured selectors and argument names without prose false positives.</summary>
    [Fact]
    public void DependencyAnalysisReportsStructuredSelectorsAndArgumentNames()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize("""
            [
              { "step": "ai.prompt", "id": "source", "args": { "prompt": "x" } },
              { "step": "ai.prompt", "id": "use", "args": { "prompt": "{{source.output.items[0].name}}", "timeout": 2 } },
              { "step": "ai.prompt", "id": "ignore", "args": { "prompt": "{{source.outputBad}} source.output" } }
            ]
            """);
        var dependency = WorkflowStepDependencyAnalyzer.FindLaterReferences(workflow, 0).Should().ContainSingle().Which;
        dependency.EffectiveId.Should().Be("use");
        dependency.ArgumentNames.Should().Equal("prompt");
    }
}
