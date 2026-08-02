using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the deterministic single-line prompt preview projection.
/// </summary>
public sealed class WorkflowPromptPreviewProjectionTests
{
    /// <summary>
    /// Verifies line feeds and tabs are projected as visible markers.
    /// </summary>
    [Fact]
    public void ControlCharactersAreProjected()
    {
        var preview = WorkflowPromptPreviewProjection.Create("a\nb\tc");

        preview.Should().Be("a ⏎ b ⇥ c");
    }

    /// <summary>
    /// Verifies long prompts are truncated with the ellipsis marker.
    /// </summary>
    [Fact]
    public void LongPromptsAreTruncated()
    {
        var preview = WorkflowPromptPreviewProjection.Create(
            new string('x', WorkflowPromptPreviewProjection.MaximumTextElements + 10));

        preview.Should().Be(
            new string('x', WorkflowPromptPreviewProjection.MaximumTextElements) +
            WorkflowPromptPreviewProjection.Ellipsis);
    }

    /// <summary>
    /// Verifies grapheme clusters are never split by truncation.
    /// </summary>
    [Fact]
    public void GraphemeClustersAreNotSplit()
    {
        var preview = WorkflowPromptPreviewProjection.Create("👩‍👩‍👧 ok");

        preview.Should().Be("👩‍👩‍👧 ok");
    }

    /// <summary>
    /// Verifies an empty prompt produces an empty preview.
    /// </summary>
    [Fact]
    public void EmptyPromptProducesEmptyPreview()
    {
        WorkflowPromptPreviewProjection.Create(string.Empty).Should().BeEmpty();
    }
}
