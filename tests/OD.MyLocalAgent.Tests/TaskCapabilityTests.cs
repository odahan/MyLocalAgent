using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies persisted task capabilities and metadata-driven long-text editing.</summary>
public sealed class TaskCapabilityTests
{
    /// <summary>Verifies all known capabilities use stable JSON strings.</summary>
    [Fact]
    public void AllCapabilitiesRoundTripAsStableStrings()
    {
        var capabilities = Enum.GetValues<TaskCapability>()
            .Where(static capability => capability != TaskCapability.None)
            .ToArray();
        var task = CreateTask() with { Capabilities = capabilities };

        var json = ScheduledTaskSerializer.Serialize(task);
        var roundTrip = ScheduledTaskSerializer.Deserialize(json);

        roundTrip.Capabilities.Should().Equal(capabilities);
        foreach (var capability in capabilities)
        {
            json.Should().Contain($"\"{capability}\"");
        }
    }

    /// <summary>Verifies legacy tasks remain valid but receive no sensitive permission.</summary>
    [Fact]
    public void MissingCapabilitiesFieldDefaultsToEmpty()
    {
        var json = ScheduledTaskSerializer.Serialize(CreateTask());
        using var document = JsonDocument.Parse(json);
        var withoutCapabilities = json.Replace("  \"capabilities\": [],\r\n", string.Empty, StringComparison.Ordinal)
            .Replace("  \"capabilities\": [],\n", string.Empty, StringComparison.Ordinal);

        ScheduledTaskSerializer.Deserialize(withoutCapabilities).Capabilities.Should().BeEmpty();
    }

    /// <summary>Verifies prompt and PowerShell text are edited atomically without changing other data.</summary>
    [Fact]
    public void LongTextEditorUsesToolMapMetadataForPromptAndPowerShell()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            """
            [
              { "step": "ai.prompt", "id": "ask", "args": { "prompt": "old prompt", "includeContext": true }, "retryCount": 2 },
              { "step": "filesystem.powerShell", "id": "script", "args": { "script": "Get-Date", "waitForExit": false } }
            ]
            """);
        var entries = WorkflowPromptEditingService.Extract(workflow);

        entries.Select(static entry => (entry.Alias, entry.ArgumentName)).Should().Equal(
            ("ai.prompt", "prompt"),
            ("filesystem.powerShell", "script"));

        var updatedText = WorkflowPromptEditingService.Apply(workflow,
        [
            new WorkflowPromptEdit(0, "new prompt", false) { ArgumentName = "prompt" },
            new WorkflowPromptEdit(1, "Get-ChildItem", false) { ArgumentName = "script" },
        ]);
        var updated = WorkflowDefinitionSerializer.Deserialize(updatedText);

        updated[0].Args.GetProperty("prompt").GetString().Should().Be("new prompt");
        updated[0].Args.GetProperty("includeContext").GetBoolean().Should().BeFalse();
        updated[0].RetryCount.Should().Be(2);
        updated[1].Args.GetProperty("script").GetString().Should().Be("Get-ChildItem");
        updated[1].Args.GetProperty("waitForExit").GetBoolean().Should().BeFalse();
    }

    private static ScheduledTask CreateTask() => new()
    {
        Id = "capability-test",
        Name = "Capability test",
        Schedule = "0 7 * * *",
        Prompt = "Test",
    };
}
