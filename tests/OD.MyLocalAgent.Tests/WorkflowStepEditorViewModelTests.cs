using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Tests deterministic visibility and scope rules of the workflow step editor form.</summary>
public sealed class WorkflowStepEditorViewModelTests
{
    /// <summary>Verifies prompt-only and AI-only sections follow the selected Tool Map definition.</summary>
    [Fact]
    public void SelectedToolControlsPromptAndGenerationSections()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.IsPromptSelected.Should().BeTrue();
        viewModel.IsAiSelected.Should().BeTrue();
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.read");
        viewModel.IsPromptSelected.Should().BeFalse();
        viewModel.IsAiSelected.Should().BeFalse();
    }

    /// <summary>Verifies existing JSON strings load as typed editor values without recursive validation.</summary>
    [Fact]
    public void ResearchDiscoverEditLoadsStringAndEnumerationValuesWithoutJsonQuotes()
    {
        const string workflow = """
            [{
              "step": "research.discover",
              "id": "discover",
              "args": {
                "themes": "[{\"terms\":[\".NET\"]}]",
                "freshnessWindow": "sliding",
                "sourceLanguage": "en",
                "language": "fr",
                "preferredDomains": "[\"learn.microsoft.com\"]",
                "minimumDocuments": 8,
                "maxResultsPerQuery": 10,
                "maxRetrievalsPerRun": 40
              }
            }]
            """;
        var viewModel = new WorkflowStepEditorViewModel();

        viewModel.Initialize(new WorkflowStepEditorRequest(
            workflow,
            workflow.IndexOf("research.discover", StringComparison.Ordinal),
            WorkflowStepEditorMode.Edit,
            [ToolGroups.Research],
            [],
            [],
            [],
            WorkflowErrorPolicy.StopOnError,
            0.2,
            1));

        viewModel.SelectedTool!.Alias.Should().Be("research.discover");
        viewModel.Arguments.Single(item => item.Definition.Name == "freshnessWindow").Value.Should().Be("sliding");
        viewModel.Arguments.Single(item => item.Definition.Name == "sourceLanguage").Value.Should().Be("en");
        viewModel.Arguments.Single(item => item.Definition.Name == "themes").Value.Should().Be("[{\"terms\":[\".NET\"]}]");
        viewModel.Arguments.Should().OnlyContain(item => string.IsNullOrEmpty(item.ValidationError));
        viewModel.HasChanges.Should().BeFalse();
    }

    /// <summary>Verifies a repair draft reports an invalid constrained value without recursively revalidating.</summary>
    [Fact]
    public void InvalidExistingEnumerationProducesOneFieldErrorWithoutRecursion()
    {
        const string workflow = """[{"step":"research.discover","id":"discover","args":{"themes":"[]","freshnessWindow":"legacy"}}]""";
        var viewModel = new WorkflowStepEditorViewModel();

        viewModel.Initialize(new WorkflowStepEditorRequest(
            workflow,
            workflow.IndexOf("research.discover", StringComparison.Ordinal),
            WorkflowStepEditorMode.Edit,
            [ToolGroups.Research],
            [],
            [],
            [],
            WorkflowErrorPolicy.StopOnError,
            0.2,
            1));

        viewModel.Arguments.Single(item => item.Definition.Name == "freshnessWindow").ValidationError.Should().Contain("sliding");
        viewModel.HasChanges.Should().BeFalse();
    }

    /// <summary>Verifies Knowledge narrowing appears only for the exact Knowledge search alias.</summary>
    [Fact]
    public void KnowledgeNarrowingRequiresExactKnowledgeSearchAlias()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.ToolAliases = "filesystem.read";
        viewModel.ShowKnowledgeTags.Should().BeFalse();
        viewModel.ToolAliases = "knowledge.search";
        viewModel.ShowKnowledgeTags.Should().BeTrue();
    }

    /// <summary>Verifies missing authorizations are proposals rather than implicit task changes.</summary>
    [Fact]
    public void MissingToolGroupStartsAsUncheckedProposal()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.read");
        viewModel.AuthorizationProposals.Should().ContainSingle(item => item.Kind == "Tool group" && !item.IsAccepted);
    }

    /// <summary>Verifies accepting a proposal counts as entered data when changing tools.</summary>
    [Fact]
    public void ToolChangeKeepsAcceptedProposalWhenDiscardIsCanceled()
    {
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(service => service.Confirm("Discard workflow step fields", It.IsAny<string>())).Returns(false);
        var viewModel = new WorkflowStepEditorViewModel(desktopInteraction: desktop.Object);
        viewModel.Initialize(Request());
        var original = ToolCatalog.Default.Find("filesystem.read")!;
        viewModel.SelectedTool = original;
        viewModel.AuthorizationProposals.Single(item => item.Kind == "Tool group").IsAccepted = true;

        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.write");

        viewModel.SelectedTool.Should().BeSameAs(original);
        viewModel.AuthorizationProposals.Should().Contain(item => item.Kind == "Tool group" && item.IsAccepted);
        desktop.VerifyAll();
    }

    /// <summary>Verifies structural Apply remains possible while missing authorizations stay opt-in warnings.</summary>
    [Fact]
    public void ProvisionalValidationAllowsUncheckedAuthorizationProposal()
    {
        var request = Request() with
        {
            DraftTask = new ScheduledTask
            {
                Id = "stage28-draft",
                Name = "Stage 28 draft",
                Schedule = "0 9 * * *",
                Prompt = "Read a file.",
                Tools = [ToolGroups.Knowledge],
            },
        };
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(request);
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.read");
        viewModel.Arguments.Single(item => item.Definition.Name == "path").Value = ".";

        viewModel.ApplyCommand.Execute(null);

        viewModel.Result.Should().NotBeNull();
        viewModel.Result!.AcceptedToolGroups.Should().BeEmpty();
        viewModel.Result.Warnings.Should().Contain(item => item.Contains("filesystem", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies a literal absolute local path becomes an unchecked proposal.</summary>
    [Fact]
    public void LiteralAbsolutePathStartsAsUncheckedProposal()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.write");
        viewModel.Arguments.Single(item => item.Definition.Name == "path").Value = @"C:\Stage28\report.txt";
        viewModel.AuthorizationProposals.Should().Contain(item => item.Kind == "Path" && !item.IsAccepted);
    }

    /// <summary>Verifies catalog numeric authoring bounds are enforced before Apply.</summary>
    [Fact]
    public void NumericEditorRejectsValueOutsideCatalogBounds()
    {
        var item = new WorkflowStepArgumentEditorItem(
            new ToolArgumentDefinition("count", WorkflowArgumentKind.Integer, true, "Count.") { Minimum = 1, Maximum = 3 }, true, "4");
        var action = () => item.Parse();
        action.Should().Throw<FormatException>().WithMessage("*between 1 and 3*");
    }

    /// <summary>Verifies string length metadata is enforced independently of the visual host.</summary>
    [Fact]
    public void StringEditorRejectsValueBeyondCatalogMaximumLength()
    {
        var item = new WorkflowStepArgumentEditorItem(
            new ToolArgumentDefinition("language", WorkflowArgumentKind.String, true, "Language.") { MaximumLength = 5 },
            true,
            "toolong");

        var action = () => item.Parse();

        action.Should().Throw<FormatException>().WithMessage("*at most 5 characters*");
        item.EditorMaximumLength.Should().Be(5);
        item.EditorHelpText.Should().Contain("Maximum: 5 characters");
    }

    /// <summary>Verifies local-path attributes project to generic file and folder editor capabilities.</summary>
    [Fact]
    public void LocalPathMetadataSelectsGenericBrowseEditors()
    {
        var fileTool = ToolCatalog.Default.Find("filesystem.read")!;
        var fileArgument = fileTool.ArgumentDefinitions.Single(item => item.Name == "path");
        var filePath = fileTool.LocalPathArguments.Single(item => item.Derivation == LocalPathDerivationKind.Direct);
        var fileItem = new WorkflowStepArgumentEditorItem(fileArgument, true, string.Empty, localPath: filePath);

        var folderTool = ToolCatalog.Default.Find("filesystem.list")!;
        var folderArgument = folderTool.ArgumentDefinitions.Single(item => item.Name == "path");
        var folderPath = folderTool.LocalPathArguments.Single(item => item.Derivation == LocalPathDerivationKind.Direct);
        var folderItem = new WorkflowStepArgumentEditorItem(folderArgument, true, string.Empty, localPath: folderPath);

        fileItem.CanBrowseFile.Should().BeTrue();
        fileItem.CanBrowseFolder.Should().BeFalse();
        fileItem.EditorHelpText.Should().Contain("File path");
        folderItem.CanBrowseFolder.Should().BeTrue();
        folderItem.CanBrowseFile.Should().BeFalse();
        folderItem.EditorHelpText.Should().Contain("Folder path");
    }

    /// <summary>Verifies reflected attributes carry authoring constraints into the immutable Tool Map.</summary>
    [Fact]
    public void ToolArgumentAttributesProjectLengthAndNumericBounds()
    {
        var tool = ToolCatalog.Default.Find("research.discover")!;

        tool.ArgumentDefinitions.Single(item => item.Name == "sourceLanguage").MaximumLength.Should().Be(35);
        tool.ArgumentDefinitions.Single(item => item.Name == "minimumDocuments").Should().Match<ToolArgumentDefinition>(
            item => item.Minimum == 1 && item.Maximum == 100);
        tool.ArgumentDefinitions.Single(item => item.Name == "maxResultsPerQuery").Should().Match<ToolArgumentDefinition>(
            item => item.Minimum == 1 && item.Maximum == 10);
    }

    /// <summary>Verifies the prompt editor loads the active provider catalog without preventing free entry.</summary>
    [Fact]
    public async Task PromptEditorLoadsActiveOllamaModelsAndKeepsConfiguredChoices()
    {
        var catalog = new Mock<IOllamaModelCatalog>(MockBehavior.Strict);
        catalog.Setup(service => service.ListInstalledModelsAsync(
                "http://localhost:11434",
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["provider-model", "shared-model"]);
        var aiOptions = new Mock<IOptionsMonitor<AIOptions>>();
        aiOptions.SetupGet(options => options.CurrentValue).Returns(new AIOptions { Provider = AIOptions.OllamaProvider });
        var ollamaOptions = new Mock<IOptionsMonitor<OllamaOptions>>();
        ollamaOptions.SetupGet(options => options.CurrentValue).Returns(new OllamaOptions
        {
            Endpoint = "http://localhost:11434",
            Model = "configured-model",
            TimeoutSeconds = 300,
        });
        var viewModel = new WorkflowStepEditorViewModel(
            ollamaModelCatalog: catalog.Object,
            aiOptions: aiOptions.Object,
            ollamaOptions: ollamaOptions.Object);
        viewModel.Initialize(Request() with { ConfiguredModel = "configured-model", TaskModel = "task-model" });
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.HasChanges = false;

        await viewModel.LoadModelChoicesAsync(CancellationToken.None);

        viewModel.ModelChoices.Should().Equal("provider-model", "shared-model", "configured-model", "task-model");
        viewModel.ModelCatalogStatus.Should().Contain("2 models exposed");
        viewModel.HasChanges.Should().BeFalse();
        viewModel.InheritModel = false;
        viewModel.UsesExplicitModel.Should().BeTrue();
        catalog.VerifyAll();
    }

    /// <summary>Verifies the insert selector filters only the immutable catalog projection.</summary>
    [Fact]
    public void ToolSearchFiltersCatalogByAliasOrDescription()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.ToolSearchText = "filesystem.write";
        viewModel.FilteredTools.Should().ContainSingle().Which.Alias.Should().Be("filesystem.write");
    }

    /// <summary>Verifies structured arguments reject primitive JSON values.</summary>
    [Fact]
    public void StructuredEditorRejectsPrimitiveJson()
    {
        var item = new WorkflowStepArgumentEditorItem(new ToolArgumentDefinition("options", WorkflowArgumentKind.Object, true, "Options."), true, "42");
        var action = () => item.Parse();
        action.Should().Throw<FormatException>().WithMessage("*object or array*");
    }

    /// <summary>Verifies a shared placeholder is inserted only into a placeholder-enabled argument.</summary>
    [Fact]
    public void PlaceholderInsertionUsesSharedCatalogAndArgumentMetadata()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        var prompt = viewModel.Arguments.Single(item => item.Definition.Name == "prompt");
        prompt.Value = "replace this text";
        prompt.SelectionStart = 8;
        prompt.SelectionLength = 4;
        prompt.SelectedPlaceholder.Should().Be(WorkflowPlaceholderCatalog.Entries.First());

        viewModel.InsertPlaceholderCommand.Execute(prompt);

        prompt.Value.Should().Be("replace {{task.id}} text");
        prompt.SelectionStart.Should().Be(19);
        prompt.SelectionLength.Should().Be(0);
    }

    /// <summary>Verifies an otherwise valid prompt tool produces an opt-in group proposal.</summary>
    [Fact]
    public void PromptToolMissingGroupProducesUncheckedProposal()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.ToolAliases = "filesystem.read";
        viewModel.AuthorizationProposals.Should().Contain(item => item.Kind == "Tool group" && item.Key == ToolGroups.FileSystem && !item.IsAccepted);
    }

    /// <summary>Verifies exact prompt aliases serialize in immutable catalog order.</summary>
    [Fact]
    public void ExactPromptAliasesSerializeInCatalogOrder()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.ToolAliases = "knowledge.search, filesystem.read";
        viewModel.ApplyCommand.Execute(null);
        var step = WorkflowDefinitionSerializer.Deserialize(viewModel.Result!.WorkflowText).Single();
        step.ToolAliases.Should().Equal(
            ToolCatalog.Default.Definitions.Where(tool => tool.IsAgentTool && new[] { "knowledge.search", "filesystem.read" }.Contains(tool.Alias)).Select(static tool => tool.Alias));
    }

    /// <summary>Verifies an explicit blank prompt model is rejected while inheritance remains valid.</summary>
    [Fact]
    public void ExplicitBlankPromptModelIsRejected()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.InheritModel = false;
        viewModel.Model = "   ";
        viewModel.ApplyCommand.Execute(null);
        viewModel.Result.Should().BeNull();
        viewModel.StatusText.Should().Contain("cannot be blank");
    }

    /// <summary>Verifies each AI generation property independently preserves inheritance.</summary>
    [Fact]
    public void GenerationOverridesAreIndependent()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.summarize");
        viewModel.OverrideTopP = true;
        viewModel.TopP = "0.75";
        viewModel.ApplyCommand.Execute(null);
        var options = WorkflowDefinitionSerializer.Deserialize(viewModel.Result!.WorkflowText).Single().GenerationOptions!;
        options.TopP.Should().Be(0.75);
        options.Temperature.Should().BeNull();
        options.ReasoningEnabled.Should().BeNull();
    }

    /// <summary>Verifies the AI-step editor persists an open-ended reasoning level.</summary>
    [Fact]
    public void GenerationOverrideAcceptsProviderSpecificReasoningEffort()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.summarize");
        viewModel.OverrideReasoning = true;
        viewModel.ReasoningEnabled = true;
        viewModel.ReasoningEffort = "xhigh";

        viewModel.ApplyCommand.Execute(null);

        var options = WorkflowDefinitionSerializer.Deserialize(viewModel.Result!.WorkflowText)
            .Single().GenerationOptions!;
        options.ReasoningEnabled.Should().BeTrue();
        options.ReasoningEffort.Should().Be("xhigh");
    }

    /// <summary>Verifies narrowing Knowledge requires a non-empty task-bounded selection.</summary>
    [Fact]
    public void EmptyKnowledgeNarrowingIsRejected()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.ToolAliases = "knowledge.search";
        viewModel.NarrowKnowledge = true;
        viewModel.ApplyCommand.Execute(null);
        viewModel.Result.Should().BeNull();
        viewModel.StatusText.Should().Contain("at least one");
    }

    /// <summary>Verifies nullable metadata preserves an explicitly included JSON null.</summary>
    [Fact]
    public void NullableArgumentPreservesExplicitNull()
    {
        var item = new WorkflowStepArgumentEditorItem(
            new ToolArgumentDefinition("value", WorkflowArgumentKind.String, false, "Value.") { AllowsNull = true }, true, "null");
        item.Parse().ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Verifies generated editor kinds use metadata rather than UI-local alias tables.</summary>
    [Theory]
    [InlineData(WorkflowArgumentKind.String, false, WorkflowArgumentEditorKind.String)]
    [InlineData(WorkflowArgumentKind.String, true, WorkflowArgumentEditorKind.MultilineString)]
    [InlineData(WorkflowArgumentKind.Integer, false, WorkflowArgumentEditorKind.Integer)]
    [InlineData(WorkflowArgumentKind.Number, false, WorkflowArgumentEditorKind.Number)]
    [InlineData(WorkflowArgumentKind.Boolean, false, WorkflowArgumentEditorKind.Boolean)]
    [InlineData(WorkflowArgumentKind.BooleanOrString, false, WorkflowArgumentEditorKind.Reasoning)]
    [InlineData(WorkflowArgumentKind.Enumeration, false, WorkflowArgumentEditorKind.Enumeration)]
    [InlineData(WorkflowArgumentKind.Object, false, WorkflowArgumentEditorKind.StructuredJson)]
    public void EditorKindComesFromCatalogMetadata(WorkflowArgumentKind kind, bool longText, WorkflowArgumentEditorKind expected)
    {
        var item = new WorkflowStepArgumentEditorItem(new ToolArgumentDefinition("value", kind, true, "Value.") { IsLongText = longText }, true, string.Empty);
        item.EditorKind.Should().Be(expected);
    }

    /// <summary>Verifies the step editor persists Stage 29 routing and structured-response fields.</summary>
    [Fact]
    public void PromptEditorPersistsNextAndResponseSchema()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.Arguments.Single(item => item.Definition.Name == "prompt").Value = "Classify {{task.prompt}}.";
        viewModel.Next = "review";
        viewModel.StructuredResponse = true;
        viewModel.ResponseSchema = """
            {
              "type": "object",
              "properties": { "category": { "type": "string" } },
              "required": ["category"]
            }
            """;

        viewModel.ApplyCommand.Execute(null);

        var step = WorkflowDefinitionSerializer.Deserialize(viewModel.Result!.WorkflowText).Single();
        step.Next.Should().Be("review");
        step.ResponseSchema.Should().NotBeNull();
        step.ResponseSchema!.Value.GetProperty("required")[0].GetString().Should().Be("category");
    }

    /// <summary>Verifies the typed step editor persists the supported forEach contract.</summary>
    [Fact]
    public void PromptEditorPersistsForEachSource()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("ai.prompt");
        viewModel.Arguments.Single(item => item.Definition.Name == "prompt").Value =
            "Summarize {{foreach.item}}.";
        viewModel.RepeatForEachItem = true;
        viewModel.ForEachSource = "{{mails.output.messages}}";

        viewModel.ApplyCommand.Execute(null);

        var step = WorkflowDefinitionSerializer.Deserialize(viewModel.Result!.WorkflowText).Single();
        step.ForEach.Should().Be("{{mails.output.messages}}");
    }

    /// <summary>Verifies repeated file writes warn when every item targets the same path.</summary>
    [Fact]
    public void RepeatedFileWriteWarnsAboutConstantPath()
    {
        var viewModel = new WorkflowStepEditorViewModel();
        viewModel.Initialize(Request());
        viewModel.SelectedTool = ToolCatalog.Default.Find("filesystem.write");
        viewModel.Arguments.Single(item => item.Definition.Name == "path").Value = "reports/mail.txt";
        viewModel.Arguments.Single(item => item.Definition.Name == "content").Value = "{{foreach.item}}";
        viewModel.RepeatForEachItem = true;
        viewModel.ForEachSource = "{{mails.output.messages}}";

        viewModel.ExecutionWarnings.Should().ContainSingle(
            warning => warning.Contains("replace the same file", StringComparison.Ordinal));
    }

    /// <summary>Verifies the decision value editor accepts a scalar JSON value.</summary>
    [Fact]
    public void DecisionValueEditorAcceptsScalarJson()
    {
        var definition = ToolCatalog.Default.Find("flow.decision")!.ArgumentDefinitions.Single(item => item.Name == "value");
        var item = new WorkflowStepArgumentEditorItem(definition, true, "0.8");

        item.Parse().GetDecimal().Should().Be(0.8m);
    }

    private static WorkflowStepEditorRequest Request() => new(
        "[]", 0, WorkflowStepEditorMode.InsertBefore, [ToolGroups.Knowledge], [], [], ["safe"], WorkflowErrorPolicy.StopOnError, 0.2, 1);
}
