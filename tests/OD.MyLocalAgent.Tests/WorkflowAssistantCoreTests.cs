using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies deterministic Stage 13 workflow formatting, validation, and requirement analysis.
/// </summary>
public sealed class WorkflowAssistantCoreTests
{
    /// <summary>
    /// Verifies the editable description preserves every normalized step field and argument value.
    /// </summary>
    [Fact]
    public void DescriptionPreservesFormsOrderIdsArgumentsAndPolicies()
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            """
            [
              "gmail.readToday",
              {
                "step": "filesystem.write",
                "id": "save",
                "args": {
                  "path": "reports/{{Today[yyyyMMdd]}}.md",
                  "content": "{{gmail.readToday.output.messages[0].content}}",
                  "metadata": { "priority": 2 }
                },
                "onError": "Retry",
                "retryCount": 2,
                "retryBackoffSeconds": 3
              }
            ]
            """);

        var first = WorkflowDescriptionFormatter.Format(workflow);
        var second = WorkflowDescriptionFormatter.Format(workflow);

        first.Should().Be(second);
        first.Should().Contain("Step 1:")
            .And.Contain("Original form: string")
            .And.Contain("Explicit id: <omitted>")
            .And.Contain("Effective id: gmail.readToday")
            .And.Contain("Produced output: {{gmail.readToday.output}}")
            .And.Contain("Step 2:")
            .And.Contain("Original form: object")
            .And.Contain("Explicit id: save")
            .And.Contain("path: \"reports/{{Today[yyyyMMdd]}}.md\"")
            .And.Contain("""metadata: {"priority":2}""");
        first.Should().Contain("onError: Retry")
            .And.Contain("retryCount: 2")
            .And.Contain("retryBackoffSeconds: 3")
            .And.Contain("Produced output: {{save.output}}");
    }

    /// <summary>
    /// Verifies strict standalone parsing rejects fences, prose, and non-array roots.
    /// </summary>
    [Theory]
    [InlineData("```json\n[]\n```")]
    [InlineData("Here is the workflow: []")]
    [InlineData("""{"step":"gmail.readToday"}""")]
    [InlineData("[")]
    public void StrictWorkflowParsingRejectsNonArrayResponse(string response)
    {
        var action = () => WorkflowDefinitionSerializer.Deserialize(response);

        action.Should().Throw<WorkflowValidationException>();
    }

    /// <summary>
    /// Verifies shared validation rejects missing arguments, wrong kinds, and invalid placeholders.
    /// </summary>
    [Theory]
    [InlineData("""[{"step":"filesystem.write","args":{"path":"out.txt"}}]""")]
    [InlineData("""[{"step":"web.search","args":{"query":"x","maxResults":"five"}}]""")]
    [InlineData("""[{"step":"filesystem.write","args":{"path":"out.txt","content":"{{future.output}}"}}]""")]
    [InlineData("""[{"step":"filesystem.write","args":{"path":"out.txt","content":"{{}}"}}]""")]
    [InlineData("""[{"step":"ai.prompt","args":{"prompt":"x","includeContext":"yes"}}]""")]
    [InlineData("""[{"step":"ai.prompt","args":{"prompt":""}}]""")]
    [InlineData("""[{"step":"ai.prompt","args":{"prompt":"   \n  "}}]""")]
    [InlineData("""[{"step":"ai.prompt","args":{"prompt":"a\r\nb"}}]""")]
    [InlineData("""[{"step":"ai.prompt","args":{"prompt":"a","prompt":"b"}}]""")]
    [InlineData("""[{"step":"ai.prompt","args":{"prompt":"a","PROMPT":"b"}}]""")]
    public void SharedValidationRejectsInvalidWorkflowDefinitions(string json)
    {
        var workflow = WorkflowDefinitionSerializer.Deserialize(json);

        var action = () => ScheduledTaskValidator.ValidateWorkflowDefinitions(
            workflow,
            [ToolGroups.FileSystem, ToolGroups.Web]);

        action.Should().Throw<WorkflowValidationException>();
    }

    /// <summary>
    /// Verifies tool, path, dynamic-path, and Safety requirements are derived from Tool Map metadata.
    /// </summary>
    [Fact]
    public void AnalyzerDerivesToolsStrongestPathsWarningsAndSafety()
    {
        var root = Path.Combine(Path.GetTempPath(), "OD.Stage13");
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            $$"""
            [
              {
                "step": "filesystem.read",
                "args": { "path": {{JsonSerializer.Serialize(Path.Combine(root, "source.txt"))}} }
              },
              {
                "step": "filesystem.write",
                "args": {
                  "path": {{JsonSerializer.Serialize(Path.Combine(root, "result.txt"))}},
                  "content": {{JsonSerializer.Serialize("{{source.output}}")}}
                }
              },
              {
                "step": "dropbox.upload",
                "args": { "localPath": {{JsonSerializer.Serialize("{{previous.output}}")}}, "dropboxPath": "/archive.txt" }
              },
              {
                "step": "filesystem.delete",
                "args": { "path": {{JsonSerializer.Serialize(Path.Combine(root, "old.txt"))}} }
              },
              "ai.summarize"
            ]
            """);

        var analysis = WorkflowRequirementAnalyzer.Analyze(
            workflow,
            [],
            [],
            []);

        analysis.RequiredToolGroups.Should().BeEquivalentTo(
            [ToolGroups.FileSystem, ToolGroups.Dropbox]);
        analysis.MissingToolGroups.Should().BeEquivalentTo(
            [ToolGroups.FileSystem, ToolGroups.Dropbox]);
        analysis.MissingPathRequirements.Should().ContainSingle(
            requirement =>
                requirement.DirectoryPath == ScheduledTaskValidator.NormalizeAccessPath(root) &&
                requirement.RequiredAccess == FileSystemAccessMode.ReadWrite);
        analysis.Warnings.Should().Contain(
            warning => warning.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
        analysis.CapabilityRequirements.Should().ContainSingle(
            requirement =>
                requirement.Capability == TaskCapability.PermanentFileDeletion &&
                !requirement.IsGranted);
    }

    /// <summary>
    /// Verifies relative paths need no task grant and an existing most-specific rule is authoritative.
    /// </summary>
    [Fact]
    public void AnalyzerHonorsRelativePathsAndMostSpecificExistingRule()
    {
        var parent = ScheduledTaskValidator.NormalizeAccessPath(
            Path.Combine(Path.GetTempPath(), "OD.Stage13.Parent"));
        var child = ScheduledTaskValidator.NormalizeAccessPath(
            Path.Combine(parent, "child"));
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            $$"""
            [
              { "step": "filesystem.read", "args": { "path": "relative.txt" } },
              { "step": "filesystem.write", "args": {
                  "path": {{JsonSerializer.Serialize(Path.Combine(child, "result.txt"))}},
                  "content": "done"
              } }
            ]
            """);

        var analysis = WorkflowRequirementAnalyzer.Analyze(
            workflow,
            [ToolGroups.FileSystem],
            [
                new FileSystemAccessRule { Path = parent, Access = FileSystemAccessMode.ReadOnly },
                new FileSystemAccessRule { Path = child, Access = FileSystemAccessMode.ReadWrite },
            ],
            []);

        analysis.MissingToolGroups.Should().BeEmpty();
        analysis.MissingPathRequirements.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies Tool Map path metadata covers every Stage 13 local-path alias.
    /// </summary>
    [Fact]
    public void ToolMapContainsRequiredLocalPathMetadata()
    {
        var aliases = new[]
        {
            "filesystem.list",
            "filesystem.read",
            "filesystem.write",
            "filesystem.createDirectory",
            "filesystem.delete",
            "filesystem.move",
            "filesystem.moveToBin",
            "filesystem.rename",
            "filesystem.setExtension",
            "filesystem.markdownToHtml",
            "filesystem.htmlToText",
            "dropbox.upload",
            "dropbox.download",
        };

        aliases.Should().OnlyContain(
            alias => ToolMap.Find(alias)!.LocalPathArguments.Count > 0);
        ToolMap.Find("filesystem.move")!.LocalPathArguments.Should().HaveCount(2);
        ToolMap.Find("filesystem.rename")!.LocalPathArguments.Should().Contain(
            item => item.Derivation == LocalPathDerivationKind.SameDirectoryLeafName);
        ToolMap.Find("filesystem.setExtension")!.LocalPathArguments.Should().Contain(
            item => item.Derivation == LocalPathDerivationKind.SameDirectoryExtension);
    }

    /// <summary>
    /// Verifies every local-path alias carries the required read or read-write level.
    /// </summary>
    [Theory]
    [InlineData("filesystem.list", FileSystemAccessMode.ReadOnly, 1)]
    [InlineData("filesystem.read", FileSystemAccessMode.ReadOnly, 1)]
    [InlineData("filesystem.write", FileSystemAccessMode.ReadWrite, 1)]
    [InlineData("filesystem.createDirectory", FileSystemAccessMode.ReadWrite, 1)]
    [InlineData("filesystem.delete", FileSystemAccessMode.ReadWrite, 1)]
    [InlineData("filesystem.move", FileSystemAccessMode.ReadWrite, 2)]
    [InlineData("filesystem.moveToBin", FileSystemAccessMode.ReadWrite, 1)]
    [InlineData("filesystem.rename", FileSystemAccessMode.ReadWrite, 2)]
    [InlineData("filesystem.setExtension", FileSystemAccessMode.ReadWrite, 2)]
    [InlineData("filesystem.htmlToText", FileSystemAccessMode.ReadOnly, 1)]
    [InlineData("dropbox.upload", FileSystemAccessMode.ReadOnly, 1)]
    [InlineData("dropbox.download", FileSystemAccessMode.ReadWrite, 1)]
    public void LocalPathMetadataHasExpectedAccess(
        string alias,
        FileSystemAccessMode access,
        int count)
    {
        var paths = ToolMap.Find(alias)!.LocalPathArguments;

        paths.Should().HaveCount(count);
        paths.Should().OnlyContain(item => item.RequiredAccess == access);
    }

    /// <summary>
    /// Verifies Markdown conversion proposes its explicit destination and warns for a dynamic one.
    /// </summary>
    [Fact]
    public void AnalyzerDerivesMarkdownDestinationAndPreservesDynamicWarning()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "OD.Stage18C.Source");
        var destinationRoot = Path.Combine(Path.GetTempPath(), "OD.Stage18C.Destination");
        var explicitWorkflow = WorkflowDefinitionSerializer.Deserialize(
            $$"""
            [{
              "step": "filesystem.markdownToHtml",
              "args": {
                "sourcePath": {{JsonSerializer.Serialize(Path.Combine(sourceRoot, "source.md"))}},
                "destinationPath": {{JsonSerializer.Serialize(Path.Combine(destinationRoot, "result.html"))}}
              }
            }]
            """);
        var dynamicWorkflow = WorkflowDefinitionSerializer.Deserialize(
            $$"""
            [{
              "step": "filesystem.markdownToHtml",
              "args": {
                "sourcePath": {{JsonSerializer.Serialize(Path.Combine(sourceRoot, "source.md"))}},
                "destinationPath": {{JsonSerializer.Serialize("{{previous.output}}")}}
              }
            }]
            """);

        var explicitAnalysis = WorkflowRequirementAnalyzer.Analyze(
            explicitWorkflow,
            [ToolGroups.FileSystem],
            [],
            []);
        var dynamicAnalysis = WorkflowRequirementAnalyzer.Analyze(
            dynamicWorkflow,
            [ToolGroups.FileSystem],
            [],
            []);

        explicitAnalysis.MissingPathRequirements.Should().Contain(item =>
            item.DirectoryPath == ScheduledTaskValidator.NormalizeAccessPath(sourceRoot) &&
            item.RequiredAccess == FileSystemAccessMode.ReadOnly);
        explicitAnalysis.MissingPathRequirements.Should().Contain(item =>
            item.DirectoryPath == ScheduledTaskValidator.NormalizeAccessPath(destinationRoot) &&
            item.RequiredAccess == FileSystemAccessMode.ReadWrite);
        dynamicAnalysis.Warnings.Should().Contain(item =>
            item.Contains("destinationPath", StringComparison.Ordinal) &&
            item.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies volume-root and UNC proposals retain their explicit review warnings.
    /// </summary>
    [Fact]
    public void AnalyzerLabelsVolumeRootAndUncProposals()
    {
        var volumeRoot = Path.GetPathRoot(Path.GetTempPath())!;
        const string uncPath = @"\\stage13-server\share\folder";
        var workflow = WorkflowDefinitionSerializer.Deserialize(
            $$"""
            [
              { "step": "filesystem.list", "args": { "path": {{JsonSerializer.Serialize(volumeRoot)}} } },
              { "step": "filesystem.list", "args": { "path": {{JsonSerializer.Serialize(uncPath)}} } }
            ]
            """);

        var analysis = WorkflowRequirementAnalyzer.Analyze(
            workflow,
            [ToolGroups.FileSystem],
            [],
            []);

        analysis.MissingPathRequirements.Should().Contain(
            item => item.IsVolumeRoot);
        analysis.MissingPathRequirements.Should().Contain(
            item => item.IsUnc);
    }
}
