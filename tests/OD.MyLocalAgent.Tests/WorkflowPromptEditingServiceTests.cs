using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies deterministic extraction and rewriting of workflow prompt arguments.
/// </summary>
public sealed class WorkflowPromptEditingServiceTests
{
    /// <summary>
    /// Verifies extraction order and position identity when identifiers are duplicated.
    /// </summary>
    [Fact]
    public void ExtractionUsesDeclarationOrderAndPositionIdentity()
    {
        var entries = Extract(
            """
            [
              { "step": "filesystem.read", "id": "source", "args": { "path": "a.txt" } },
              { "step": "ai.prompt", "id": "review", "args": { "prompt": "first" } },
              { "step": "ai.summarize" },
              { "step": "ai.prompt", "id": "review", "args": { "prompt": "second", "includeContext": true } }
            ]
            """);

        entries.Should().HaveCount(2);
        entries[0].Position.Should().Be(1);
        entries[0].DisplayPosition.Should().Be(2);
        entries[0].PromptText.Should().Be("first");
        entries[0].IncludeContext.Should().BeFalse("omission means false at runtime");
        entries[0].PriorStepIds.Should().Equal("source");
        entries[1].Position.Should().Be(3);
        entries[1].PromptText.Should().Be("second");
        entries[1].IncludeContext.Should().BeTrue();
        entries[1].PriorStepIds.Should().Equal("source", "review", "ai.summarize");
    }

    /// <summary>
    /// Verifies that implicit identifiers are reported as such.
    /// </summary>
    [Fact]
    public void ImplicitIdentifierIsReported()
    {
        var entry = Extract("""[ { "step": "ai.prompt", "args": { "prompt": "x" } } ]""").Single();

        entry.ExplicitId.Should().BeNull();
        entry.EffectiveId.Should().Be("ai.prompt");
        entry.IsImplicitId.Should().BeTrue();
    }

    /// <summary>
    /// Verifies repair extraction for string form and for a missing prompt argument.
    /// </summary>
    /// <param name="json">The workflow JSON to extract.</param>
    [Theory]
    [InlineData("""[ "ai.prompt" ]""")]
    [InlineData("""[ { "step": "ai.prompt" } ]""")]
    [InlineData("""[ { "step": "ai.prompt", "args": { "prompt": null } } ]""")]
    [InlineData("""[ { "step": "ai.prompt", "args": { "prompt": 42 } } ]""")]
    public void RepairCasesExtractWithEmptyWorkingText(string json)
    {
        var entry = Extract(json).Single();

        entry.IsRepairCase.Should().BeTrue();
        entry.PromptText.Should().BeEmpty();
        entry.IncludeContext.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that carriage returns are normalized and flagged as an effective modification.
    /// </summary>
    [Fact]
    public void CarriageReturnsAreNormalizedAndFlagged()
    {
        var entry = Extract(
            """[ { "step": "ai.prompt", "args": { "prompt": "a\r\nb\rc\nd  " } } ]""").Single();

        entry.RequiresLineEndingNormalization.Should().BeTrue();
        entry.PromptText.Should().Be("a\nb\nc\nd  ", "no other whitespace is changed");
    }

    /// <summary>
    /// Verifies that LF-canonical text is not flagged for normalization.
    /// </summary>
    [Fact]
    public void CanonicalTextIsNotFlagged()
    {
        var entry = Extract(
            """[ { "step": "ai.prompt", "args": { "prompt": "a\nb" } } ]""").Single();

        entry.RequiresLineEndingNormalization.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a string-form step is promoted to object form on apply.
    /// </summary>
    [Fact]
    public void StringFormIsPromotedToObjectForm()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize("""[ "ai.prompt" ]""");

        var applied = WorkflowPromptEditingService.Apply(
            workflow,
            [new WorkflowPromptEdit(0, "Repaired.", IncludeContext: true)]);

        var step = WorkflowDefinitionSerializer.Deserialize(applied).Single();
        step.Step.Should().Be("ai.prompt");
        step.Args.GetProperty("prompt").GetString().Should().Be("Repaired.");
        step.Args.GetProperty("includeContext").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Verifies that unrelated arguments and step metadata survive a targeted rewrite.
    /// </summary>
    [Fact]
    public void UnrelatedPropertiesArePreserved()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            """
            [
              {
                "step": "ai.prompt",
                "id": "review",
                "args": { "before": 1, "prompt": "old", "after": [ "x" ] },
                "onError": "ContinueOnError",
                "retryCount": 3,
                "retryBackoffSeconds": 5,
                "generationOptions": { "temperature": 0.4, "topP": 0.95 }
              }
            ]
            """);

        var applied = WorkflowPromptEditingService.Apply(
            workflow,
            [new WorkflowPromptEdit(0, "new", IncludeContext: false)]);

        var step = WorkflowDefinitionSerializer.Deserialize(applied).Single();
        step.Id.Should().Be("review");
        step.OnError.Should().Be(WorkflowErrorPolicy.ContinueOnError);
        step.RetryCount.Should().Be(3);
        step.RetryBackoffSeconds.Should().Be(5);
        step.GenerationOptions!.Temperature.Should().Be(0.4);
        step.GenerationOptions.TopP.Should().Be(0.95);
        step.Args.EnumerateObject().Select(static property => property.Name)
            .Should().Equal("before", "prompt", "after", "includeContext");
        step.Args.GetProperty("before").GetInt32().Should().Be(1);
        step.Args.GetProperty("after")[0].GetString().Should().Be("x");
        step.Args.GetProperty("prompt").GetString().Should().Be("new");
        step.Args.GetProperty("includeContext").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Verifies batch application across several positions with duplicate identifiers.
    /// </summary>
    [Fact]
    public void BatchApplyTargetsPositionsNotIdentifiers()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            """
            [
              { "step": "ai.prompt", "id": "same", "args": { "prompt": "one" } },
              { "step": "ai.prompt", "id": "same", "args": { "prompt": "two" } }
            ]
            """);

        var applied = WorkflowPromptEditingService.Apply(
            workflow,
            [
                new WorkflowPromptEdit(1, "second", IncludeContext: true),
                new WorkflowPromptEdit(0, "first", IncludeContext: false),
            ]);

        var steps = WorkflowDefinitionSerializer.Deserialize(applied);
        steps[0].Args.GetProperty("prompt").GetString().Should().Be("first");
        steps[0].Args.GetProperty("includeContext").GetBoolean().Should().BeFalse();
        steps[1].Args.GetProperty("prompt").GetString().Should().Be("second");
        steps[1].Args.GetProperty("includeContext").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Verifies that applying to a non-prompt position is rejected.
    /// </summary>
    [Fact]
    public void ApplyRejectsNonPromptPositions()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize("""[ "ai.summarize" ]""");

        var act = () => WorkflowPromptEditingService.Apply(
            workflow,
            [new WorkflowPromptEdit(0, "x", IncludeContext: true)]);

        act.Should().Throw<WorkflowValidationException>();
    }

    /// <summary>
    /// Verifies multiline extract, apply, and extract losslessness.
    /// </summary>
    [Fact]
    public void MultilineRoundTripIsLossless()
    {
        const string prompt =
            "You are reviewing the working notes of the day.\n\nRules:\n"
            + "- Use only the supplied context; never invent a fact.\n\n"
            + "Produce a summary and the next actions.";
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            $$"""[ { "step": "ai.prompt", "args": { "prompt": {{JsonSerializer.Serialize(prompt)}} } } ]""");

        var extracted = WorkflowPromptEditingService.Extract(workflow).Single();
        extracted.PromptText.Should().Be(prompt);

        var applied = WorkflowPromptEditingService.Apply(
            workflow,
            [new WorkflowPromptEdit(0, extracted.PromptText, extracted.IncludeContext)]);
        var reextracted = WorkflowPromptEditingService
            .Extract(WorkflowDefinitionSerializer.Deserialize(applied))
            .Single();

        reextracted.PromptText.Should().Be(prompt);
        WorkflowPromptEditingService.Apply(
            WorkflowDefinitionSerializer.Deserialize(applied),
            [new WorkflowPromptEdit(0, reextracted.PromptText, reextracted.IncludeContext)])
            .Should().Be(applied);
    }

    /// <summary>
    /// Verifies that a multiline prompt occupies exactly one physical JSON line.
    /// </summary>
    [Fact]
    public void MultilinePromptStaysOnOnePhysicalLine()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize("""[ "ai.prompt" ]""");

        var applied = WorkflowPromptEditingService.Apply(
            workflow,
            [new WorkflowPromptEdit(0, "line one\nline two\nline three", IncludeContext: true)]);

        var promptLines = applied
            .Split('\n')
            .Where(static line => line.Contains("\"prompt\"", StringComparison.Ordinal))
            .ToArray();

        promptLines.Should().ContainSingle();
        promptLines[0].Should().Contain("line one\\nline two\\nline three");
    }

    /// <summary>
    /// Verifies duplication detection for context-bearing placeholders.
    /// </summary>
    /// <param name="prompt">The prompt text to inspect.</param>
    [Theory]
    [InlineData("Review {{previous.output}}.")]
    [InlineData("Review {{previous.output.items[0].name}}.")]
    [InlineData("Review {{source.output}}.")]
    [InlineData("Review {{source.output.title}}.")]
    [InlineData("Review {{workflow.context}}.")]
    public void DuplicationIsDetectedForOutputPlaceholders(string prompt)
    {
        WorkflowPromptEditingService.ReferencesWorkflowOutput(prompt, ["source"])
            .Should().BeTrue();
    }

    /// <summary>
    /// Verifies that task and date placeholders never trigger the duplication warning.
    /// </summary>
    /// <param name="prompt">The prompt text to inspect.</param>
    [Theory]
    [InlineData("Task {{task.name}} on {{Today}} at {{Now}}.")]
    [InlineData("Prompt {{task.prompt}} for {{task.id}}.")]
    [InlineData("Plain text without placeholders.")]
    [InlineData("")]
    public void DuplicationIsNotDetectedForTaskOrDatePlaceholders(string prompt)
    {
        WorkflowPromptEditingService.ReferencesWorkflowOutput(prompt, ["source"])
            .Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an unknown identifier does not trigger the duplication warning.
    /// </summary>
    [Fact]
    public void DuplicationIgnoresUnknownIdentifiers()
    {
        WorkflowPromptEditingService.ReferencesWorkflowOutput("{{later.output}}", ["source"])
            .Should().BeFalse();
    }

    /// <summary>
    /// Verifies that detection never mutates the inspected values.
    /// </summary>
    [Fact]
    public void DuplicationDetectionNeverMutates()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            """[ { "step": "ai.prompt", "args": { "prompt": "Review {{previous.output}}.", "includeContext": true } } ]""");

        var entry = WorkflowPromptEditingService.Extract(workflow).Single();
        WorkflowPromptEditingService
            .ReferencesWorkflowOutput(entry.PromptText, entry.PriorStepIds)
            .Should().BeTrue();

        entry.PromptText.Should().Be("Review {{previous.output}}.");
        entry.IncludeContext.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the selected-output-only shape of section 13.3.
    /// </summary>
    [Fact]
    public void SelectedOutputOnlyShapeIsSupported()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            """
            [
              { "step": "filesystem.read", "id": "source", "args": { "path": "a.txt" } },
              { "step": "ai.prompt", "id": "review", "args": { "prompt": "old", "includeContext": true } }
            ]
            """);

        var applied = WorkflowPromptEditingService.Apply(
            workflow,
            [new WorkflowPromptEdit(1, "Review only this result:\n\n{{source.output}}", false)]);

        var step = WorkflowDefinitionSerializer.Deserialize(applied)[1];
        step.Args.GetProperty("includeContext").GetBoolean().Should().BeFalse();
        step.Args.GetProperty("prompt").GetString()
            .Should().Be("Review only this result:\n\n{{source.output}}");
    }

    private static IReadOnlyList<WorkflowPromptEntry> Extract(string json) =>
        WorkflowPromptEditingService.Extract(
            WorkflowDefinitionSerializer.Deserialize(json));
}
