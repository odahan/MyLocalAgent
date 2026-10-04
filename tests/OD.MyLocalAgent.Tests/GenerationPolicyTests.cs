using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.ViewModels;
using OllamaSharp.Models;
using ThinkValue = OllamaSharp.Models.Chat.ThinkValue;
using OpenAIChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies Stage 14 generation contracts, precedence, strict persistence, and provider mapping.
/// </summary>
public sealed class GenerationPolicyTests
{
    private static readonly AIGenerationDefaults Defaults = new()
    {
        Temperature = 0.2,
        TopP = 0.95,
        ContextWindowTokens = 12_345,
    };

    /// <summary>
    /// Verifies every property resolves independently through step, task, and Settings.
    /// </summary>
    [Theory]
    [InlineData(null, null, 0.2, GenerationOptionSource.Settings)]
    [InlineData(0.4, null, 0.4, GenerationOptionSource.Task)]
    [InlineData(null, 0.8, 0.8, GenerationOptionSource.Step)]
    [InlineData(0.4, 0.8, 0.8, GenerationOptionSource.Step)]
    public void ResolverUsesFieldWisePrecedence(
        double? taskTemperature,
        double? stepTemperature,
        double expected,
        GenerationOptionSource source)
    {
        var policy = GenerationPolicyResolver.Resolve(
            Defaults,
            new GenerationOptions
            {
                Temperature = taskTemperature,
                TopP = 0.7,
            },
            new GenerationOptions
            {
                Temperature = stepTemperature,
            });

        policy.Temperature.Should().Be(expected);
        policy.TemperatureSource.Should().Be(source);
        policy.TopP.Should().Be(0.7);
        policy.TopPSource.Should().Be(GenerationOptionSource.Task);
        policy.ContextWindowTokens.Should().Be(12_345);
    }

    /// <summary>
    /// Verifies fixed internal policies ignore user sampling defaults but retain global context.
    /// </summary>
    [Theory]
    [MemberData(nameof(InternalPolicies))]
    public void InternalPoliciesRemainFixed(InternalGenerationPolicy internalPolicy)
    {
        var policy = GenerationPolicyResolver.ResolveInternal(
            internalPolicy,
            Defaults with { Temperature = 1.8, TopP = 0.2 });

        policy.Temperature.Should().Be(0);
        policy.TopP.Should().Be(1);
        policy.ContextWindowTokens.Should().Be(12_345);
        policy.TemperatureSource.Should().Be(GenerationOptionSource.Program);
        policy.TopPSource.Should().Be(GenerationOptionSource.Program);
    }

    /// <summary>Verifies internal categories independently honor their Settings thinking mode.</summary>
    [Fact]
    public void InternalPoliciesUseConfiguredThinkingCategory()
    {
        var providerDefault = GenerationPolicyResolver.ResolveInternal(
            InternalGenerationPolicies.WorkflowAuthoring,
            Defaults with
            {
                InternalThinking = new InternalThinkingOptions
                {
                    WorkflowAuthoring = new InternalThinkingSetting
                    {
                        Mode = InternalThinkingMode.ProviderDefault,
                    },
                },
            });
        var specificLevel = GenerationPolicyResolver.ResolveInternal(
            InternalGenerationPolicies.GuardrailScreening,
            Defaults with
            {
                InternalThinking = new InternalThinkingOptions
                {
                    Guardrails = new InternalThinkingSetting
                    {
                        Mode = InternalThinkingMode.SpecificLevel,
                        Effort = "high",
                    },
                },
            });

        providerDefault.UseProviderReasoningDefault.Should().BeTrue();
        specificLevel.ReasoningEnabled.Should().BeTrue();
        specificLevel.ReasoningEffort.Should().Be("high");
    }

    /// <summary>
    /// Gets all fixed program-owned policies.
    /// </summary>
    public static TheoryData<InternalGenerationPolicy> InternalPolicies => new()
    {
        InternalGenerationPolicies.WorkflowAuthoring,
        InternalGenerationPolicies.WorkflowExplanation,
        InternalGenerationPolicies.GuardrailScreening,
        InternalGenerationPolicies.GuardrailReduction,
    };

    /// <summary>
    /// Verifies invalid global and override ranges are rejected without clamping.
    /// </summary>
    [Theory]
    [InlineData(double.NaN, 1, 8192)]
    [InlineData(double.PositiveInfinity, 1, 8192)]
    [InlineData(-0.01, 1, 8192)]
    [InlineData(2.01, 1, 8192)]
    [InlineData(0.2, 0, 8192)]
    [InlineData(0.2, 1.01, 8192)]
    [InlineData(0.2, 1, 1023)]
    [InlineData(0.2, 1, 2_000_001)]
    public void InvalidDefaultsAreRejected(
        double temperature,
        double topP,
        int context)
    {
        var error = GenerationPolicyValidation.ValidateDefaults(
            new AIGenerationDefaults
            {
                Temperature = temperature,
                TopP = topP,
                ContextWindowTokens = context,
            });

        error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies existing task JSON remains valid and does not acquire an override.
    /// </summary>
    [Fact]
    public void ExistingTaskWithoutOverridesRemainsCanonical()
    {
        var task = ScheduledTaskSerializer.Deserialize(ValidTaskJson());

        task.GenerationOptions.Should().BeNull();
        ScheduledTaskSerializer.Serialize(task)
            .Should().NotContain("generationOptions");
    }

    /// <summary>
    /// Verifies partial task and AI-step overrides round-trip while empty objects are omitted.
    /// </summary>
    [Fact]
    public void PartialOverridesRoundTripAndEmptyObjectsAreOmitted()
    {
        var task = CreateTask() with
        {
            GenerationOptions = new GenerationOptions { Temperature = 0.1 },
            Workflow =
            [
                new WorkflowStepDefinition
                {
                    Step = "ai.prompt",
                    Id = "draft",
                    Args = JsonSerializer.SerializeToElement(
                        new { prompt = "Draft a title." }),
                    GenerationOptions =
                        new GenerationOptions { TopP = 0.8 },
                },
            ],
        };

        var json = ScheduledTaskSerializer.Serialize(task);
        var roundTrip = ScheduledTaskSerializer.Deserialize(json);

        roundTrip.GenerationOptions!.Temperature.Should().Be(0.1);
        roundTrip.GenerationOptions.TopP.Should().BeNull();
        var stepOptions = roundTrip.Workflow[0].GenerationOptions;
        stepOptions.Should().NotBeNull();
        stepOptions!.Temperature.Should().BeNull();
        stepOptions.TopP.Should().Be(0.8);

        var empty = task with
        {
            GenerationOptions = new GenerationOptions(),
            Workflow =
            [
                new WorkflowStepDefinition
                {
                    Step = "ai.summarize",
                    IsStringForm = true,
                    GenerationOptions = new GenerationOptions(),
                },
            ],
        };
        ScheduledTaskSerializer.Serialize(empty)
            .Should().NotContain("generationOptions");
    }

    /// <summary>
    /// Verifies the task-wide reasoning preference survives JSON persistence.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TaskReasoningPreferenceRoundTrips(bool reasoningEnabled)
    {
        var task = CreateTask() with
        {
            GenerationOptions = new GenerationOptions
            {
                ReasoningEnabled = reasoningEnabled,
            },
        };

        var json = ScheduledTaskSerializer.Serialize(task);
        var roundTrip = ScheduledTaskSerializer.Deserialize(json);

        json.Should().Contain(
            $"\"reasoningEnabled\": {reasoningEnabled.ToString().ToLowerInvariant()}");
        roundTrip.GenerationOptions!.ReasoningEnabled.Should().Be(reasoningEnabled);
    }

    /// <summary>Verifies an open-ended reasoning effort survives canonical persistence.</summary>
    [Fact]
    public void TaskReasoningEffortRoundTrips()
    {
        var task = CreateTask() with
        {
            GenerationOptions = new GenerationOptions
            {
                ReasoningEnabled = true,
                ReasoningEffort = "xhigh",
            },
        };

        var json = ScheduledTaskSerializer.Serialize(task);
        var roundTrip = ScheduledTaskSerializer.Deserialize(json);

        json.Should().Contain("\"reasoningEffort\": \"xhigh\"");
        roundTrip.GenerationOptions!.ReasoningEffort.Should().Be("xhigh");
    }

    /// <summary>Verifies a nearer reasoning switch replaces the entire inherited setting.</summary>
    [Fact]
    public void ResolverTreatsReasoningAsOneAtomicOverride()
    {
        var policy = GenerationPolicyResolver.Resolve(
            Defaults,
            new GenerationOptions
            {
                ReasoningEnabled = true,
                ReasoningEffort = "high",
            },
            new GenerationOptions { ReasoningEnabled = true });

        policy.ReasoningEnabled.Should().BeTrue();
        policy.ReasoningEffort.Should().BeNull();
    }

    /// <summary>Verifies thinking inherits from Settings when a task has no override.</summary>
    [Fact]
    public void ResolverUsesSettingsThinkingWhenTaskDoesNotOverrideIt()
    {
        var policy = GenerationPolicyResolver.Resolve(
            Defaults with
            {
                ReasoningEnabled = true,
                ReasoningEffort = "high",
            });

        policy.ReasoningEnabled.Should().BeTrue();
        policy.ReasoningEffort.Should().Be("high");
    }

    /// <summary>Verifies a task-level switch takes priority over disabled Settings thinking.</summary>
    [Fact]
    public void ResolverUsesTaskThinkingBeforeSettingsThinking()
    {
        var policy = GenerationPolicyResolver.Resolve(
            Defaults with { ReasoningEnabled = false },
            new GenerationOptions { ReasoningEnabled = true });

        policy.ReasoningEnabled.Should().BeTrue();
        policy.ReasoningEffort.Should().BeNull();
    }

    /// <summary>
    /// Verifies every Tool Map AI alias accepts an object-form generation override.
    /// </summary>
    [Fact]
    public void EveryAiAliasRoundTripsGenerationOptions()
    {
        foreach (var definition in ToolMap.Definitions.Where(
                     static item => item.IsAiStep))
        {
            var arguments = definition.Alias == ToolMap.PromptAlias
                ? JsonSerializer.SerializeToElement(new { prompt = "Test" })
                : JsonSerializer.SerializeToElement(new { text = "Test" });
            var workflow = new[]
            {
                new WorkflowStepDefinition
                {
                    Step = definition.Alias,
                    Args = arguments,
                    GenerationOptions =
                        new GenerationOptions { Temperature = 0.6 },
                },
            };

            var json = WorkflowDefinitionSerializer.Serialize(workflow);
            var parsed = WorkflowDefinitionSerializer.Deserialize(json);

            parsed.Single().GenerationOptions!.Temperature.Should().Be(
                0.6,
                definition.Alias);
        }
    }

    /// <summary>
    /// Verifies every deterministic Tool Map alias rejects generation metadata.
    /// </summary>
    [Fact]
    public void EveryNonAiAliasRejectsGenerationOptions()
    {
        foreach (var definition in ToolMap.Definitions.Where(
                     static item => !item.IsAiStep))
        {
            var workflow = new[]
            {
                new WorkflowStepDefinition
                {
                    Step = definition.Alias,
                    GenerationOptions =
                        new GenerationOptions { Temperature = 0.4 },
                },
            };

            var action = () =>
                ScheduledTaskValidator.ValidateWorkflowDefinitions(
                    workflow,
                    ToolGroups.All);

            action.Should()
                .Throw<WorkflowValidationException>()
                .WithMessage("*generationOptions*not an AI step*");
        }
    }

    /// <summary>
    /// Verifies nulls, unknown properties, invalid JSON types, and invalid context windows fail.
    /// </summary>
    [Theory]
    [InlineData("""null""")]
    [InlineData("""{"temperature":"0.2"}""")]
    [InlineData("""{"temperature":null}""")]
    [InlineData("""{"topP":false}""")]
    [InlineData("""{"reasoningEnabled":"false"}""")]
    [InlineData("""{"reasoningEnabled":false,"reasoningEffort":"high"}""")]
    [InlineData("""{"reasoningEffort":" high "}""")]
    [InlineData("""{"contextWindowTokens":1023}""")]
    [InlineData("""{"contextWindowTokens":2000001}""")]
    [InlineData("""{"contextWindowTokens":8192.5}""")]
    [InlineData("""{"contextWindowTokens":null}""")]
    [InlineData("""{"contextWindowTokens":"8192"}""")]
    [InlineData("""{"unknown":1}""")]
    public void InvalidGenerationJsonFailsDeterministically(
        string generationOptions)
    {
        var json = ValidTaskJson().Replace(
            "\"prompt\": \"Test\",",
            $"\"prompt\": \"Test\", \"generationOptions\": {generationOptions},",
            StringComparison.Ordinal);

        var action = () => ScheduledTaskSerializer.Deserialize(json);

        action.Should().Throw<TaskValidationException>();
    }

    /// <summary>
    /// Verifies context-window properties outside generationOptions cannot enter persistence.
    /// </summary>
    [Theory]
    [InlineData(
        """{"step":"ai.prompt","args":{"prompt":"x"},"contextWindowTokens":4096}""")]
    public void WorkflowContextWindowPropertiesAreRejected(string step)
    {
        var action = () =>
            WorkflowDefinitionSerializer.Deserialize($"[{step}]");

        action.Should().Throw<WorkflowValidationException>();
    }

    /// <summary>
    /// Verifies decimal editing accepts comma and dot input under French and English cultures.
    /// </summary>
    [Theory]
    [InlineData("fr-FR", "0,25", 0.25)]
    [InlineData("fr-FR", "0.25", 0.25)]
    [InlineData("en-US", "0.25", 0.25)]
    [InlineData("en-US", "0,25", 0.25)]
    public void DecimalEditingIsCultureSafe(
        string cultureName,
        string text,
        double expected)
    {
        var parsed = SettingsViewModel.TryParseCultureSafeDouble(
            text,
            CultureInfo.GetCultureInfo(cultureName),
            out var value);

        parsed.Should().BeTrue();
        value.Should().Be(expected);
    }

    /// <summary>
    /// Verifies policy application preserves caller options and maps context only for Ollama.
    /// </summary>
    [Fact]
    public async Task PolicyWrapperPreservesOptionsAndMapsProviderHonestly()
    {
        var callerOptions = new ChatOptions
        {
            ModelId = "caller-model",
            Temperature = 1.7f,
            TopP = 0.4f,
            Tools = [],
            ResponseFormat = ChatResponseFormat.Json,
        };
        var ollamaCapture = new StubChatClient("ok");
        var ollama = new GenerationPolicyChatClient(
            ollamaCapture,
            GenerationPolicyResolver.Resolve(Defaults),
            AIOptions.OllamaProvider);

        _ = await ollama.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "test")],
            callerOptions,
            CancellationToken.None);

        callerOptions.Temperature.Should().Be(1.7f);
        callerOptions.TopP.Should().Be(0.4f);
        ollamaCapture.LastOptions.Should().NotBeSameAs(callerOptions);
        ollamaCapture.LastOptions!.Temperature.Should().Be(0.2f);
        ollamaCapture.LastOptions.TopP.Should().Be(0.95f);
        ollamaCapture.LastOptions.ModelId.Should().Be("caller-model");
        ollamaCapture.LastOptions.Tools.Should().NotBeNull();
        ollamaCapture.LastOptions.ResponseFormat.Should().BeSameAs(
            ChatResponseFormat.Json);
        ollamaCapture.LastOptions.AdditionalProperties.Should()
            .ContainKey(OllamaOption.NumCtx.Name);
        ollamaCapture.LastOptions.AdditionalProperties.Should()
            .ContainKey(OllamaOption.Think.Name);
        ollamaCapture.LastOptions.Reasoning!.Effort.Should().Be(ReasoningEffort.Medium);

        var openAICapture = new StubChatClient("ok");
        var openAI = new GenerationPolicyChatClient(
            openAICapture,
            GenerationPolicyResolver.Resolve(Defaults),
            AIOptions.OpenAIProvider);
        _ = await openAI.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "test")],
            callerOptions,
            CancellationToken.None);

        openAICapture.LastOptions!.Temperature.Should().Be(0.2f);
        openAICapture.LastOptions.TopP.Should().Be(0.95f);
        openAICapture.LastOptions.Reasoning!.Effort.Should().Be(ReasoningEffort.Medium);
        (openAICapture.LastOptions.AdditionalProperties?.ContainsKey(
            OllamaOption.NumCtx.Name) ?? false).Should().BeFalse();
    }

    /// <summary>Verifies custom effort keywords are passed verbatim to each provider protocol.</summary>
    [Fact]
    public async Task PolicyWrapperPassesCustomEffortVerbatim()
    {
        var policy = GenerationPolicyResolver.Resolve(
            Defaults,
            new GenerationOptions
            {
                ReasoningEnabled = true,
                ReasoningEffort = "xhigh",
            });
        var ollamaCapture = new StubChatClient("ok");
        var ollama = new GenerationPolicyChatClient(
            ollamaCapture,
            policy,
            AIOptions.OllamaProvider);

        _ = await ollama.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "test")],
            cancellationToken: CancellationToken.None);

        ollamaCapture.LastOptions!.AdditionalProperties.Should()
            .ContainKey(OllamaOption.Think.Name)
            .WhoseValue.Should().BeOfType<ThinkValue>()
            .Which.ToString().Should().Be("xhigh");
        ollamaCapture.LastOptions.Reasoning!.Effort.Should().BeNull();

        var openAICapture = new StubChatClient("ok");
        var openAI = new GenerationPolicyChatClient(
            openAICapture,
            policy,
            AIOptions.OpenAIProvider);
        _ = await openAI.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "test")],
            cancellationToken: CancellationToken.None);

        object? raw = openAICapture.LastOptions!.RawRepresentationFactory!(openAICapture);
        var completionOptions = raw.Should().BeOfType<OpenAIChatCompletionOptions>().Subject;
#pragma warning disable OPENAI001
        completionOptions.ReasoningEffortLevel.ToString().Should().Be("xhigh");
#pragma warning restore OPENAI001
    }

    /// <summary>Verifies unsupported reasoning controls are retried without provider options.</summary>
    [Fact]
    public async Task PolicyWrapperFallsBackWhenProviderRejectsReasoning()
    {
        var capture = new StubChatClient("ok")
        {
            ResponseFactory = call => call == 1
                ? throw new HttpRequestException("Unsupported reasoning parameter.")
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")),
        };
        var client = new GenerationPolicyChatClient(
            capture,
            GenerationPolicyResolver.Resolve(Defaults),
            AIOptions.OpenAIProvider,
            reasoningEnabled: false);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "test")],
            cancellationToken: CancellationToken.None);

        response.Text.Should().Be("ok");
        capture.ResponseCallCount.Should().Be(2);
        capture.OptionCalls[0]!.Reasoning!.Effort.Should().Be(ReasoningEffort.None);
        capture.OptionCalls[1]!.Reasoning.Should().BeNull();
    }

    /// <summary>Verifies streaming retries safely before any provider content is emitted.</summary>
    [Fact]
    public async Task StreamingPolicyFallsBackBeforeFirstUpdate()
    {
        var capture = new StubChatClient("ok")
        {
            StreamingExceptionFactory = call => call == 1
                ? new HttpRequestException("reasoning_effort is unsupported")
                : null,
        };
        var client = new GenerationPolicyChatClient(
            capture,
            GenerationPolicyResolver.Resolve(Defaults),
            AIOptions.OpenAIProvider,
            reasoningEnabled: false);
        var text = new StringBuilder();

        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "test")],
            cancellationToken: CancellationToken.None))
        {
            text.Append(update.Text);
        }

        text.ToString().Should().Be("ok");
        capture.StreamingCallCount.Should().Be(2);
        capture.OptionCalls[0]!.Reasoning!.Effort.Should().Be(ReasoningEffort.None);
        capture.OptionCalls[1]!.Reasoning.Should().BeNull();
    }

    /// <summary>
    /// Verifies a temporary model selection supersedes a caller model without mutating its options.
    /// </summary>
    [Fact]
    public async Task TemporaryModelSelectionOverridesOnlyTheScopedCall()
    {
        var capture = new StubChatClient("ok");
        var client = new ModelOverrideChatClient(capture, "temporary-model");
        var callerOptions = new ChatOptions { ModelId = "default-model" };

        _ = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "test")],
            callerOptions,
            CancellationToken.None);

        callerOptions.ModelId.Should().Be("default-model");
        capture.LastOptions!.ModelId.Should().Be("temporary-model");
    }

    /// <summary>
    /// Verifies task and AI-step model selections survive canonical JSON round-tripping.
    /// </summary>
    [Fact]
    public void TemporaryModelSelectionsRoundTrip()
    {
        var task = CreateTask() with
        {
            Model = "task-model",
            Workflow =
            [
                new WorkflowStepDefinition
                {
                    Step = "ai.prompt",
                    Id = "review",
                    Args = JsonSerializer.SerializeToElement(new { prompt = "Review." }),
                    Model = "step-model",
                },
            ],
        };

        var roundTrip = ScheduledTaskSerializer.Deserialize(ScheduledTaskSerializer.Serialize(task));

        roundTrip.Model.Should().Be("task-model");
        roundTrip.Workflow.Single().Model.Should().Be("step-model");
    }

    private static ScheduledTask CreateTask() =>
        new()
        {
            Id = "generation-test",
            Name = "Generation test",
            Schedule = "0 7 * * *",
            Prompt = "Test",
            Tools = [],
            FileSystemAccess = [],
            Workflow = [],
            Enabled = true,
            ErrorPolicy = WorkflowErrorPolicy.StopOnError,
        };

    private static string ValidTaskJson() =>
        """
        {
          "id": "generation-test",
          "name": "Generation test",
          "schedule": "0 7 * * *",
          "prompt": "Test",
          "tools": [],
          "fileSystemAccess": [],
          "workflow": [],
          "enabled": true,
          "errorPolicy": "StopOnError"
        }
        """;
}
