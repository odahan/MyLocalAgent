using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies caret-aware structured and verbatim workflow insertion.
/// </summary>
public sealed class WorkflowCaretInsertionServiceTests
{
    private const string TwoStepWorkflow =
        """
        [
          {
            "step": "filesystem.read",
            "id": "source",
            "args": {
              "path": "inbox/source.txt"
            }
          },
          "ai.summarize"
        ]
        """;

    /// <summary>
    /// Verifies the normative example of section 13.1.
    /// </summary>
    [Fact]
    public void CaretInsideFirstElementInsertsAfterIt()
    {
        var caret = TwoStepWorkflow.IndexOf("filesystem.read", StringComparison.Ordinal);

        var result = Insert(TwoStepWorkflow, caret);

        result.IsStructured.Should().BeTrue();
        result.Warning.Should().BeNull();
        result.InsertedIndex.Should().Be(1);
        result.CanonicalFormattingApplied.Should().BeTrue();
        result.Text.Should().Be(
            """
            [
              {
                "step": "filesystem.read",
                "id": "source",
                "args": {
                  "path": "inbox/source.txt"
                }
              },
              {
                "step": "filesystem.write",
                "id": "filesystem-write",
                "args": {
                  "path": "",
                  "content": ""
                }
              },
              "ai.summarize"
            ]
            """);
        result.Text[result.CaretIndex].Should().Be('{');
        result.Text[result.CaretIndex..].Should().Contain("filesystem.write");
    }

    /// <summary>
    /// Verifies that a caret before the first element inserts at index zero.
    /// </summary>
    /// <param name="caret">The caret index.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CaretBeforeFirstElementInsertsAtIndexZero(int caret)
    {
        var result = Insert(TwoStepWorkflow, caret);

        result.InsertedIndex.Should().Be(0);
        result.IsStructured.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the decision that a caret on the first element start inserts at index zero.
    /// </summary>
    [Fact]
    public void CaretOnFirstElementStartInsertsAtIndexZero()
    {
        var caret = TwoStepWorkflow.IndexOf('{', 1);

        var result = Insert(TwoStepWorkflow, caret);

        result.InsertedIndex.Should().Be(0);
    }

    /// <summary>
    /// Verifies that a caret between elements inserts after the element on the left.
    /// </summary>
    [Fact]
    public void CaretBetweenElementsInsertsAfterTheLeftElement()
    {
        var caret = TwoStepWorkflow.IndexOf("},", StringComparison.Ordinal) + 2;

        var result = Insert(TwoStepWorkflow, caret);

        result.InsertedIndex.Should().Be(1);
    }

    /// <summary>
    /// Verifies that a caret after the last element appends.
    /// </summary>
    [Fact]
    public void CaretAfterLastElementAppends()
    {
        var result = Insert(TwoStepWorkflow, TwoStepWorkflow.Length);

        result.InsertedIndex.Should().Be(2);
    }

    /// <summary>
    /// Verifies that an out-of-range caret is clamped rather than rejected.
    /// </summary>
    /// <param name="caret">The requested caret index.</param>
    /// <param name="expectedIndex">The expected insertion index.</param>
    [Theory]
    [InlineData(-50, 0)]
    [InlineData(int.MaxValue, 2)]
    public void OutOfRangeCaretIsClamped(int caret, int expectedIndex)
    {
        Insert(TwoStepWorkflow, caret).InsertedIndex.Should().Be(expectedIndex);
    }

    /// <summary>
    /// Verifies insertion into an empty array.
    /// </summary>
    [Fact]
    public void EmptyArrayInsertsAtIndexZero()
    {
        var result = Insert("[]", 1);

        result.IsStructured.Should().BeTrue();
        result.InsertedIndex.Should().Be(0);
        result.Text[result.CaretIndex].Should().Be('{');
    }

    /// <summary>
    /// Verifies that a caret inside a JSON string still selects the containing element.
    /// </summary>
    [Fact]
    public void CaretInsideStringElementInsertsAfterIt()
    {
        var caret = TwoStepWorkflow.IndexOf("summarize", StringComparison.Ordinal);

        Insert(TwoStepWorkflow, caret).InsertedIndex.Should().Be(2);
    }

    /// <summary>
    /// Verifies that the returned caret is obtained by reparsing identical serialized steps.
    /// </summary>
    [Fact]
    public void ReturnedCaretIsResolvedByPositionNotByStringSearch()
    {
        var first = Insert("[]", 0);
        var second = Insert(first.Text, first.Text.Length);

        second.InsertedIndex.Should().Be(1);
        second.Text[second.CaretIndex].Should().Be('{');
        second.Text[..second.CaretIndex].Should().Contain("filesystem-write\"");
        second.CaretIndex.Should().BeGreaterThan(first.CaretIndex);
    }

    /// <summary>
    /// Verifies UTF-16 to UTF-8 caret mapping for non-ASCII source text.
    /// </summary>
    /// <param name="marker">Non-ASCII content placed before the caret.</param>
    [Theory]
    [InlineData("éàü")]
    [InlineData("日本語のテキスト")]
    [InlineData("🙂🙃")]
    public void NonAsciiTextMapsCaretCorrectly(string marker)
    {
        var source = $$"""
            [
              { "step": "filesystem.read", "args": { "path": "{{marker}}" } },
              "ai.summarize"
            ]
            """;
        var caret = source.IndexOf("ai.summarize", StringComparison.Ordinal);

        var result = Insert(source, caret);

        result.IsStructured.Should().BeTrue();
        result.InsertedIndex.Should().Be(2);
        result.Text[result.CaretIndex].Should().Be('{');
    }

    /// <summary>
    /// Verifies that a caret inside a surrogate pair moves to the preceding scalar boundary.
    /// </summary>
    [Fact]
    public void CaretInsideSurrogatePairMovesToScalarBoundary()
    {
        const string source = "[ \"🙂\" ]";
        var pairStart = source.IndexOf('\uD83D');

        WorkflowCaretInsertionService.ClampCaret(source, pairStart + 1)
            .Should().Be(pairStart);
    }

    /// <summary>
    /// Verifies that CRLF source JSON is mapped and returned canonically.
    /// </summary>
    [Fact]
    public void CarriageReturnSourceIsMappedAndReturnedCanonically()
    {
        var source = TwoStepWorkflow.Replace("\n", "\r\n", StringComparison.Ordinal);
        var caret = source.IndexOf("ai.summarize", StringComparison.Ordinal);

        var result = Insert(source, caret);

        result.IsStructured.Should().BeTrue();
        result.InsertedIndex.Should().Be(2);
        result.Text.Should().Be(
            WorkflowDefinitionSerializer.Serialize(
                WorkflowDefinitionSerializer.Deserialize(result.Text)));
        result.Text[result.CaretIndex].Should().Be('{');
    }

    /// <summary>
    /// Verifies the verbatim fallback for unparseable input.
    /// </summary>
    [Fact]
    public void InvalidJsonReceivesVerbatimSnippetAndExactWarning()
    {
        const string source = "[ { \"step\": ";

        var result = Insert(source, source.Length);

        result.IsStructured.Should().BeFalse();
        result.CanonicalFormattingApplied.Should().BeFalse();
        result.InsertedIndex.Should().Be(-1);
        result.Warning.Should().Be(
            "Workflow JSON is invalid. The snippet was inserted as text; add or repair JSON separators manually.");
        result.Text.Should().StartWith(source);
        result.CaretIndex.Should().Be(source.Length);
        result.Text[result.CaretIndex].Should().Be('{');
    }

    /// <summary>
    /// Verifies that whitespace-only input uses the fallback path.
    /// </summary>
    /// <param name="source">The unparseable source text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{ \"step\": \"filesystem.read\" }")]
    public void UnparseableInputUsesFallback(string source)
    {
        var result = Insert(source, source.Length);

        result.IsStructured.Should().BeFalse();
        result.Warning.Should().Be(WorkflowCaretInsertionService.FallbackWarning);
    }

    /// <summary>
    /// Verifies that fallback insertion preserves surrounding text and indents continuations.
    /// </summary>
    [Fact]
    public void FallbackIndentsContinuationLinesAndPreservesSurroundings()
    {
        const string source = "[ { \"step\":\n    tail";
        var caret = source.Length - "tail".Length;

        var result = Insert(source, caret);

        result.Text.Should().StartWith("[ { \"step\":\n    {");
        result.Text.Should().EndWith("tail");
        result.Text.Should().Contain($"{Environment.NewLine}      \"step\": \"filesystem.write\"");
        result.CaretIndex.Should().Be(caret);
    }

    /// <summary>
    /// Verifies that the fallback does not invent separators around the snippet.
    /// </summary>
    [Fact]
    public void FallbackDoesNotInventSeparators()
    {
        const string source = "[ { \"step\": ";

        var result = Insert(source, source.Length);

        result.Text[source.Length..].Should().StartWith("{");
        result.Text.Should().NotContain($",{Environment.NewLine}{{");
    }

    private static WorkflowCaretInsertionResult Insert(string source, int caret)
    {
        var workflow = TryDeserialize(source);
        var step = WorkflowSnippetBuilder.Create(
            ToolMap.Find("filesystem.write")!,
            new WorkflowSnippetOptions(),
            workflow,
            WorkflowErrorPolicy.StopOnError,
            temperature: 0,
            topP: 0);
        return WorkflowCaretInsertionService.Insert(source, caret, step);
    }

    private static IReadOnlyList<WorkflowStepDefinition> TryDeserialize(string source)
    {
        try
        {
            return WorkflowDefinitionSerializer.Deserialize(source);
        }
        catch (Exception exception)
            when (exception is WorkflowValidationException or ArgumentException)
        {
            return [];
        }
    }
}
