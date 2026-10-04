using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.ViewModels;
using OllamaSharp.Models;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies context inheritance, persistence, and provider mapping.</summary>
public sealed class ContextWindowOverrideTests
{
    /// <summary>Loads task context and propagates unsaved changes without overriding inherited reasoning.</summary>
    [Fact]
    public async Task TaskEditorPropagatesContextOnlyChanges()
    {
        var task = new ScheduledTask
        {
            Id = "context-editor",
            Name = "Context editor",
            Schedule = "0 7 * * *",
            Prompt = "Test",
            Tools = [],
            GenerationOptions = new GenerationOptions { ContextWindowTokens = 32768 },
        };
        var store = new Mock<ITaskStore>();
        store.Setup(s => s.GetAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        var messenger = new StrongReferenceMessenger();
        EditPromptMessage? snapshot = null;
        messenger.Register<EditPromptMessage>(new object(), (_, message) => snapshot = message);
        var editor = new TaskEditorViewModel(store.Object, Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(), Mock.Of<IDesktopInteractionService>(), messenger, TimeProvider.System);
        editor.Open(task.Id);
        await (editor.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        editor.UseGlobalContextWindow.Should().BeFalse();
        editor.ContextWindowOverrideText.Should().Be("32768");
        editor.ContextWindowOverrideText = "65536";
        editor.IsDirty.Should().BeTrue();
        editor.OpenPromptCommand.Execute(null);
        snapshot!.GenerationOptions!.ContextWindowTokens.Should().Be(65536);
        snapshot.GenerationOptions.ReasoningEnabled.Should().BeNull();

        editor.UseGlobalContextWindow = true;
        editor.OpenPromptCommand.Execute(null);
        snapshot!.GenerationOptions.Should().BeNull();
        editor.EffectiveContextWindowText.Should().Contain("Settings");
        editor.UseGlobalContextWindow = false;
        editor.ContextWindowOverrideText = string.Empty;
        editor.OpenPromptCommand.Execute(null);
        snapshot!.GenerationOptions.Should().BeNull();
    }

    /// <summary>Passes the resolved context to Ollama for every generation round.</summary>
    [Fact]
    public async Task OllamaReceivesStepContext()
    {
        var capture = new StubChatClient("ok");
        var client = new GenerationPolicyChatClient(capture,
            GenerationPolicyResolver.Resolve(new AIGenerationDefaults(),
                new GenerationOptions { ContextWindowTokens = 32768 },
                new GenerationOptions { ContextWindowTokens = 16384 }),
            AIOptions.OllamaProvider);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        capture.LastOptions!.AdditionalProperties![OllamaOption.NumCtx.Name].Should().Be(16384);
    }

    /// <summary>Edits and clears a context-only step override without changing other metadata.</summary>
    [Fact]
    public void StepEditorPreservesAndClearsContextOverride()
    {
        const string workflow = """
            [{"step":"ai.prompt","id":"draft","args":{"prompt":"Write","includeContext":false},
              "generationOptions":{"contextWindowTokens":16384}}]
            """;
        var request = new WorkflowStepEditorRequest(workflow, workflow.IndexOf("ai.prompt", StringComparison.Ordinal),
            WorkflowStepEditorMode.Edit, [], [], [], [], WorkflowErrorPolicy.StopOnError, 0.2, 1)
        {
            ContextWindowTokens = 32768,
            ContextWindowSource = GenerationOptionSource.Task,
        };
        var editor = new WorkflowStepEditorViewModel();
        editor.Initialize(request);
        editor.OverrideContextWindow.Should().BeTrue();
        editor.ContextWindowTokens.Should().Be("16384");
        editor.ContextWindowTokens = "65536";
        editor.ApplyCommand.Execute(null);
        WorkflowDefinitionSerializer.Deserialize(editor.Result!.WorkflowText)[0]
            .GenerationOptions!.ContextWindowTokens.Should().Be(65536);

        editor.Initialize(request);
        editor.OverrideContextWindow = false;
        editor.EffectiveContextWindowText.Should().Contain("Task");
        editor.ApplyCommand.Execute(null);
        WorkflowDefinitionSerializer.Deserialize(editor.Result!.WorkflowText)[0].GenerationOptions.Should().BeNull();
    }

    /// <summary>Resolves context independently from sampling and reasoning overrides.</summary>
    [Theory]
    [InlineData(null, null, 8192, GenerationOptionSource.Settings)]
    [InlineData(32768, null, 32768, GenerationOptionSource.Task)]
    [InlineData(32768, 16384, 16384, GenerationOptionSource.Step)]
    [InlineData(null, 65536, 65536, GenerationOptionSource.Step)]
    public void ContextUsesStepTaskSettingsPrecedence(
        int? taskContext, int? stepContext, int expected, GenerationOptionSource source)
    {
        var policy = GenerationPolicyResolver.Resolve(
            new AIGenerationDefaults { ContextWindowTokens = 8192, ReasoningEnabled = false },
            new GenerationOptions { ContextWindowTokens = taskContext, Temperature = 0.4 },
            new GenerationOptions { ContextWindowTokens = stepContext, TopP = 0.8 });

        policy.ContextWindowTokens.Should().Be(expected);
        policy.ContextWindowSource.Should().Be(source);
        policy.Temperature.Should().Be(0.4);
        policy.TopP.Should().Be(0.8);
        policy.ReasoningEnabled.Should().BeFalse();
    }

    /// <summary>Preserves context-only overrides during task and workflow round trips.</summary>
    [Fact]
    public void ContextOnlyOverridesRoundTrip()
    {
        var task = ScheduledTaskSerializer.Deserialize("""
            {
              "id": "context-test", "name": "Context test", "schedule": "0 7 * * *",
              "tools": [], "generationOptions": { "contextWindowTokens": 32768 },
              "workflow": [
                { "step": "ai.prompt", "id": "draft", "args": { "prompt": "Write" },
                  "generationOptions": { "contextWindowTokens": 16384 } }
              ]
            }
            """);
        var restored = ScheduledTaskSerializer.Deserialize(ScheduledTaskSerializer.Serialize(task));

        restored.GenerationOptions!.IsEmpty.Should().BeFalse();
        restored.GenerationOptions.ContextWindowTokens.Should().Be(32768);
        restored.Workflow[0].GenerationOptions!.ContextWindowTokens.Should().Be(16384);
        restored.GenerationOptions.ReasoningEnabled.Should().BeNull();
        using var json = JsonDocument.Parse(ScheduledTaskSerializer.Serialize(restored));
        json.RootElement.GetProperty("generationOptions").GetProperty("contextWindowTokens")
            .GetInt32().Should().Be(32768);
    }
}
