using FluentAssertions;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the shared model-information placeholder contract.
/// </summary>
public sealed class ModelPromptPlaceholderResolverTests
{
    private static readonly ModelPromptPlaceholderContext Context = new(
        "qwen3:14b",
        new EffectiveGenerationPolicy(
            0.25,
            0.9,
            32_768,
            GenerationOptionSource.Task,
            GenerationOptionSource.Step));

    /// <summary>
    /// Verifies all four placeholders use deterministic invariant formatting.
    /// </summary>
    [Fact]
    public void ResolvesEveryModelPlaceholder()
    {
        var result = ModelPromptPlaceholderResolver.Resolve(
            "{{ModelName}}|{{ModelSettings}}|{{ModelContextSize}}|{{ModelInfo}}",
            Context);

        result.Should().Be(
            "qwen3:14b|Temp 0.25 - TopP 0.9|32768|" +
            "ModelName qwen3:14b - Temp 0.25 - TopP 0.9 - Context 32768");
    }

    /// <summary>
    /// Verifies names are case-insensitive and unrelated markers remain exact.
    /// </summary>
    [Fact]
    public void ResolvesCaseInsensitivelyAndPreservesOtherMarkers()
    {
        const string unknown = "{{  Customer_Value  }}";

        var result = ModelPromptPlaceholderResolver.Resolve(
            $"{{{{ modelname }}}} {unknown} {{{{MODELSETTINGS}}}}",
            Context);

        result.Should().Be(
            $"qwen3:14b {unknown} Temp 0.25 - TopP 0.9");
    }

    /// <summary>
    /// Verifies replacement text is not scanned recursively.
    /// </summary>
    [Fact]
    public void ReplacementIsNonRecursive()
    {
        var context = Context with { };
        var recursiveName = new ModelPromptPlaceholderContext(
            "model-{{ModelContextSize}}",
            new EffectiveGenerationPolicy(
                context.Temperature,
                context.TopP,
                context.ContextWindowTokens,
                GenerationOptionSource.Settings,
                GenerationOptionSource.Settings));

        ModelPromptPlaceholderResolver.Resolve("{{ModelName}}", recursiveName)
            .Should().Be("model-{{ModelContextSize}}");
    }
}
