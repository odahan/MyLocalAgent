using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the deterministic workflow snippet builder.
/// </summary>
public sealed class WorkflowSnippetBuilderTests
{
    /// <summary>
    /// Verifies that a step without arguments emits an empty argument object.
    /// </summary>
    [Fact]
    public void NoArgumentStepEmitsEmptyArguments()
    {
        var step = Build("calendar.today", new WorkflowSnippetOptions());

        step.Step.Should().Be("calendar.today");
        step.Id.Should().Be("calendar-today");
        step.Args.EnumerateObject().Should().BeEmpty();
        step.OnError.Should().BeNull();
        step.GenerationOptions.Should().BeNull();
    }

    /// <summary>
    /// Verifies that optional arguments are omitted unless explicitly requested.
    /// </summary>
    [Fact]
    public void OptionalArgumentsAreOmittedByDefault()
    {
        var step = Build("gmail.readRecent", new WorkflowSnippetOptions());

        ArgumentNames(step).Should().Equal("daysBack");
    }

    /// <summary>
    /// Verifies that optional arguments are emitted with typed placeholder values.
    /// </summary>
    [Fact]
    public void OptionalArgumentsUseTypedPlaceholders()
    {
        var step = Build(
            "gmail.readRecent",
            new WorkflowSnippetOptions { IncludeOptionalArguments = true });

        ArgumentNames(step).Should().Equal("daysBack", "maxMessages");
        step.Args.GetProperty("daysBack").GetInt32().Should().Be(0);
        step.Args.GetProperty("maxMessages").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// Verifies that an enumeration argument uses its first allowed value.
    /// </summary>
    [Fact]
    public void EnumerationArgumentUsesFirstAllowedValue()
    {
        var step = Build("calendar.read", new WorkflowSnippetOptions());

        step.Args.GetProperty("period").GetString().Should().Be("Day");
    }

    /// <summary>
    /// Verifies that path and safety-gated steps emit their required arguments in order.
    /// </summary>
    [Fact]
    public void SafetyGatedPathStepEmitsRequiredArgumentsInOrder()
    {
        var step = Build("filesystem.delete", new WorkflowSnippetOptions());

        ArgumentNames(step).Should().Equal("path");
        step.Args.GetProperty("path").GetString().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a built-in AI skill omits its optional text argument by default.
    /// </summary>
    [Fact]
    public void BuiltInAiSkillOmitsOptionalText()
    {
        var step = Build("ai.summarize", new WorkflowSnippetOptions());

        ArgumentNames(step).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the default shape of a new prompt step.
    /// </summary>
    [Fact]
    public void PromptStepUsesDefaultPromptAndContext()
    {
        var step = Build(ToolMap.PromptAlias, new WorkflowSnippetOptions());

        ArgumentNames(step).Should().Equal("prompt", "includeContext");
        step.Args.GetProperty("prompt").GetString()
            .Should().Be(WorkflowSnippetBuilder.DefaultPromptText);
        step.Args.GetProperty("includeContext").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the context option is independent of the optional-argument option.
    /// </summary>
    /// <param name="includeOptional">Whether optional arguments are requested.</param>
    /// <param name="includeContext">Whether the accumulated context is requested.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PromptContextIsIndependentOfOptionalArguments(
        bool includeOptional,
        bool includeContext)
    {
        var step = Build(
            ToolMap.PromptAlias,
            new WorkflowSnippetOptions
            {
                IncludeOptionalArguments = includeOptional,
                IncludeAccumulatedContext = includeContext,
            });

        ArgumentNames(step).Should().Equal("prompt", "includeContext");
        step.Args.GetProperty("includeContext").GetBoolean().Should().Be(includeContext);
    }

    /// <summary>
    /// Verifies the emitted error-policy block.
    /// </summary>
    [Fact]
    public void ErrorPolicyBlockUsesTheCurrentTaskPolicy()
    {
        var step = Build(
            "filesystem.read",
            new WorkflowSnippetOptions { IncludeErrorPolicy = true },
            errorPolicy: WorkflowErrorPolicy.ContinueOnError);

        step.OnError.Should().Be(WorkflowErrorPolicy.ContinueOnError);
        step.RetryCount.Should().Be(1);
        step.RetryBackoffSeconds.Should().Be(2);
    }

    /// <summary>
    /// Verifies that generation options are emitted only for AI steps.
    /// </summary>
    [Fact]
    public void GenerationOptionsAreEmittedForAiStepsOnly()
    {
        var options = new WorkflowSnippetOptions { IncludeGenerationOptions = true };

        var aiStep = Build(ToolMap.PromptAlias, options, temperature: 0.4, topP: 0.95);
        aiStep.GenerationOptions.Should().NotBeNull();
        aiStep.GenerationOptions!.Temperature.Should().Be(0.4);
        aiStep.GenerationOptions.TopP.Should().Be(0.95);

        var toolStep = Build("filesystem.read", options, temperature: 0.4, topP: 0.95);
        toolStep.GenerationOptions.Should().BeNull();
    }

    /// <summary>
    /// Verifies unique-id generation against string and object forms, case-insensitively.
    /// </summary>
    [Fact]
    public void UniqueIdAvoidsStringAndObjectFormsCaseInsensitively()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            """
            [
              "filesystem-write",
              { "step": "filesystem.read", "id": "FILESYSTEM-WRITE-2" }
            ]
            """);

        WorkflowSnippetBuilder.CreateUniqueId("filesystem.write", workflow)
            .Should().Be("filesystem-write-3");
    }

    /// <summary>
    /// Verifies that repeated insertions of one alias produce increasing suffixes.
    /// </summary>
    [Fact]
    public void RepeatedInsertionsProduceIncreasingSuffixes()
    {
        var workflow = new List<WorkflowStepDefinition>();

        for (var index = 0; index < 3; index++)
        {
            workflow.Add(Build("filesystem.write", new WorkflowSnippetOptions(), workflow));
        }

        workflow.Select(static step => step.EffectiveId)
            .Should().Equal("filesystem-write", "filesystem-write-2", "filesystem-write-3");
    }

    private static IEnumerable<string> ArgumentNames(WorkflowStepDefinition step) =>
        step.Args.ValueKind == JsonValueKind.Object
            ? step.Args.EnumerateObject().Select(static property => property.Name)
            : [];

    private static WorkflowStepDefinition Build(
        string alias,
        WorkflowSnippetOptions options,
        IReadOnlyList<WorkflowStepDefinition>? workflow = null,
        WorkflowErrorPolicy errorPolicy = WorkflowErrorPolicy.StopOnError,
        double temperature = 0.2,
        double topP = 1) =>
        WorkflowSnippetBuilder.Create(
            ToolMap.Find(alias)!,
            options,
            workflow ?? [],
            errorPolicy,
            temperature,
            topP);
}
