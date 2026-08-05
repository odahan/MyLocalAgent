using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the Stage 17 Tool Map argument metadata and the structured placeholder catalog.
/// </summary>
public sealed class ToolMapMetadataTests
{
    /// <summary>
    /// Verifies that every entry carries a description and complete argument metadata.
    /// </summary>
    [Fact]
    public void EveryDefinitionExposesCompleteMetadata()
    {
        foreach (var definition in ToolMap.Definitions)
        {
            definition.Description.Should().NotBeNullOrWhiteSpace(
                $"alias '{definition.Alias}' must document its operation");
            definition.Description.Should().EndWith(
                ".",
                $"alias '{definition.Alias}' must document one English sentence");

            foreach (var argument in definition.ArgumentDefinitions)
            {
                argument.Name.Should().NotBeNullOrWhiteSpace();
                argument.Description.Should().NotBeNullOrWhiteSpace(
                    $"argument '{definition.Alias}.{argument.Name}' must be documented");
                argument.Example.Should().NotBeNullOrWhiteSpace(
                    $"argument '{definition.Alias}.{argument.Name}' must show an example");

                if (argument.Kind == WorkflowArgumentKind.Enumeration)
                {
                    argument.AllowedValues.Should().NotBeEmpty(
                        $"argument '{definition.Alias}.{argument.Name}' is an enumeration");
                }
                else
                {
                    argument.AllowedValues.Should().BeEmpty(
                        $"argument '{definition.Alias}.{argument.Name}' is not an enumeration");
                }

                if (argument.Kind is WorkflowArgumentKind.Integer
                    or WorkflowArgumentKind.Number
                    or WorkflowArgumentKind.Boolean
                    or WorkflowArgumentKind.Object)
                {
                    argument.SupportsPlaceholders.Should().BeFalse(
                        $"argument '{definition.Alias}.{argument.Name}' is not JSON-string bound");
                }
            }
        }
    }

    /// <summary>
    /// Verifies that the legacy name projections stay derived from the argument metadata.
    /// </summary>
    [Fact]
    public void NameProjectionsMatchArgumentDefinitions()
    {
        foreach (var definition in ToolMap.Definitions)
        {
            definition.Arguments.Should().Equal(
                definition.ArgumentDefinitions.Select(static argument => argument.Name),
                $"alias '{definition.Alias}' projects ordered argument names");
            definition.OptionalArguments.Should().Equal(
                definition.ArgumentDefinitions
                    .Where(static argument => !argument.IsRequired)
                    .Select(static argument => argument.Name),
                $"alias '{definition.Alias}' projects ordered optional argument names");
        }
    }

    /// <summary>
    /// Verifies that agent tool metadata matches the reflected method contract exactly.
    /// </summary>
    [Fact]
    public void AgentToolMetadataMatchesReflection()
    {
        var checkedArguments = 0;

        foreach (var definition in ToolMap.Definitions.Where(static item => item.IsAgentTool))
        {
            var method = definition.DeclaringType.GetMethod(
                definition.MethodName!,
                BindingFlags.Instance | BindingFlags.Public);
            method.Should().NotBeNull($"alias '{definition.Alias}' must map to a real method");

            definition.Description.Should().Be(
                method!.GetCustomAttribute<DescriptionAttribute>()?.Description,
                $"alias '{definition.Alias}' shares the reflected tool description");

            var parameters = method.GetParameters()
                .Where(static parameter => parameter.ParameterType != typeof(CancellationToken))
                .ToArray();
            definition.ArgumentDefinitions.Should().HaveCount(
                parameters.Length,
                $"alias '{definition.Alias}' declares every runtime parameter");

            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                var argument = definition.ArgumentDefinitions[index];
                checkedArguments++;

                argument.Name.Should().Be(parameter.Name);
                argument.IsRequired.Should().Be(
                    !parameter.HasDefaultValue,
                    $"argument '{definition.Alias}.{argument.Name}' optionality is reflected");
                argument.Description.Should().Be(
                    parameter.GetCustomAttribute<DescriptionAttribute>()?.Description,
                    $"argument '{definition.Alias}.{argument.Name}' shares the reflected description");

                var parameterType = Nullable.GetUnderlyingType(parameter.ParameterType)
                    ?? parameter.ParameterType;
                argument.Kind.Should().Be(
                    ExpectedKind(parameterType),
                    $"argument '{definition.Alias}.{argument.Name}' kind is reflected");

                if (argument.Kind == WorkflowArgumentKind.Enumeration)
                {
                    argument.AllowedValues.Should().Equal(
                        Enum.GetNames(parameterType),
                        $"argument '{definition.Alias}.{argument.Name}' lists reflected enum names");
                }
            }
        }

        checkedArguments.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verifies the explicit runtime contract of the workflow-only AI entries.
    /// </summary>
    [Fact]
    public void AiEntriesDeclareTheirRuntimeContract()
    {
        foreach (var definition in ToolMap.Definitions.Where(static item => item.IsAiStep))
        {
            definition.ToolGroup.Should().Be("ai");
            definition.AgentFunctionName.Should().BeNull();
            definition.MethodName.Should().BeNull();
            definition.SafetyFlag.Should().BeNull();
            definition.LocalPathArguments.Should().BeEmpty();
            ToolGroups.All.Should().NotContain(
                definition.ToolGroup,
                "the synthetic ai group is never whitelistable");
        }

        var prompt = ToolMap.Find(ToolMap.PromptAlias);
        prompt.Should().NotBeNull();
        prompt!.Arguments.Should().Equal("prompt", "includeContext");
        prompt.OptionalArguments.Should().Equal("includeContext");
        prompt.ArgumentDefinitions[0].Kind.Should().Be(WorkflowArgumentKind.String);
        prompt.ArgumentDefinitions[1].Kind.Should().Be(WorkflowArgumentKind.Boolean);
        prompt.ArgumentDefinitions[1].SupportsPlaceholders.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the corrected optional text projection of the built-in AI skills.
    /// </summary>
    /// <param name="alias">The built-in AI skill alias.</param>
    [Theory]
    [InlineData("ai.summarize")]
    [InlineData("ai.extractActionItems")]
    [InlineData("ai.classifyEmail")]
    public void BuiltInAiSkillsTreatTextAsOptional(string alias)
    {
        var definition = ToolMap.Find(alias);

        definition.Should().NotBeNull();
        definition!.Arguments.Should().Equal("text");
        definition.OptionalArguments.Should().Equal("text");
        definition.ArgumentDefinitions.Should().ContainSingle()
            .Which.IsRequired.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that non-AI aliases keep their previous argument contract exactly.
    /// </summary>
    [Fact]
    public void NonAiArgumentContractIsUnchanged()
    {
        var expected = new Dictionary<string, (string[] Arguments, string[] Optional)>(
            StringComparer.Ordinal)
        {
            ["gmail.readToday"] = (["maxMessages"], ["maxMessages"]),
            ["gmail.readUnread"] = (["maxMessages"], ["maxMessages"]),
            ["gmail.readRecent"] = (["daysBack", "maxMessages"], ["maxMessages"]),
            ["gmail.send"] = (["to", "subject", "body"], []),
            ["calendar.today"] = ([], []),
            ["calendar.read"] = (["period"], []),
            ["calendar.create"] = (["summary", "start", "end", "description"], []),
            ["dropbox.upload"] = (["localPath", "dropboxPath"], []),
            ["dropbox.download"] = (["dropboxPath", "localPath"], []),
            ["dropbox.list"] = (["folderPath"], []),
            ["filesystem.list"] = (["path"], []),
            ["filesystem.read"] = (["path"], []),
            ["filesystem.write"] = (["path", "content"], []),
            ["filesystem.move"] = (["from", "to"], []),
            ["filesystem.moveToBin"] = (["path"], []),
            ["filesystem.rename"] = (["path", "newName"], []),
            ["filesystem.setExtension"] = (["path", "extension"], []),
            ["filesystem.delete"] = (["path"], []),
            ["filesystem.createDirectory"] = (["path"], []),
            ["filesystem.markdownToHtml"] = (["sourcePath", "destinationPath", "theme"], ["destinationPath", "theme"]),
            ["filesystem.htmlToText"] = (["sourcePath", "format"], ["format"]),
            ["sqlite.listObjects"] = (["databasePath", "objectType", "namePattern", "maxResults"], ["objectType", "namePattern", "maxResults"]),
            ["sqlite.describeTable"] = (["databasePath", "tableName"], []),
            ["sqlite.query"] = (["databasePath", "sql", "parameters", "maxRows", "storageSpaceId"], ["parameters", "maxRows", "storageSpaceId"]),
            ["sqlite.execute"] = (["databasePath", "sql", "parameters", "createIfMissing"], ["parameters", "createIfMissing"]),
            ["http.get"] = (["url"], []),
            ["http.post"] = (["url", "body", "contentType"], []),
            ["http.getText"] = (["url"], []),
            ["web.search"] = (["query", "maxResults"], ["maxResults"]),
        };

        var actual = ToolMap.Definitions
            .Where(static item => !item.IsAiStep)
            .ToArray();

        actual.Select(static item => item.Alias).Should().BeEquivalentTo(expected.Keys);
        foreach (var definition in actual)
        {
            var (arguments, optional) = expected[definition.Alias];
            definition.Arguments.Should().Equal(arguments, $"alias '{definition.Alias}'");
            definition.OptionalArguments.Should().Equal(optional, $"alias '{definition.Alias}'");
        }
    }

    /// <summary>
    /// Verifies that the placeholder text projection stays derived from the structured entries.
    /// </summary>
    [Fact]
    public void PlaceholderDefinitionsAreDerivedFromEntries()
    {
        WorkflowPlaceholderCatalog.Entries.Should().NotBeEmpty();
        WorkflowPlaceholderCatalog.Definitions.Should().Equal(
            WorkflowPlaceholderCatalog.Entries
                .Select(static entry => $"{entry.DisplayText} - {entry.Description}"));

        foreach (var entry in WorkflowPlaceholderCatalog.Entries)
        {
            entry.InsertText.Should().StartWith("{{").And.EndWith("}}");
            entry.Description.Should().NotBeNullOrWhiteSpace();
            entry.RequiresPriorStepId.Should().Be(
                entry.InsertText.Contains("stepId", StringComparison.Ordinal),
                $"placeholder '{entry.InsertText}' declares its prior-step requirement");
        }
    }

    /// <summary>
    /// Verifies that the documented placeholder text projection is unchanged.
    /// </summary>
    [Fact]
    public void PlaceholderDefinitionsKeepTheirDocumentedText()
    {
        WorkflowPlaceholderCatalog.Definitions.Should().Equal(
            "{{task.id}} - current task id",
            "{{task.name}} - current task name",
            "{{task.prompt}} - current task prompt",
            "{{previous.output}} - previous successful step output",
            "{{stepId.output}} - a prior named step output",
            "{{stepId.output.property}} and {{stepId.output.items[0].name}} - structured JSON selection",
            "{{workflow.context}} - ordered successful outputs before the current step",
            "{{ModelName}} - active model name",
            "{{ModelSettings}} - effective Temperature and Top P",
            "{{ModelContextSize}} - declared context-window size in tokens",
            "{{ModelInfo}} - active model name and effective generation settings",
            "{{Today}} or {{Today[custom .NET format]}} - local date",
            "{{Now}} or {{Now[custom .NET format]}} - local date and time");
    }

    private static WorkflowArgumentKind ExpectedKind(Type type)
    {
        if (type == typeof(System.Text.Json.JsonElement))
        {
            return WorkflowArgumentKind.Object;
        }

        if (type.IsEnum)
        {
            return WorkflowArgumentKind.Enumeration;
        }

        if (type == typeof(bool))
        {
            return WorkflowArgumentKind.Boolean;
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return WorkflowArgumentKind.Number;
        }

        if (type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong))
        {
            return WorkflowArgumentKind.Integer;
        }

        return WorkflowArgumentKind.String;
    }
}
