using FluentAssertions;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies Settings utilities and unsaved-change interactions.
/// </summary>
public sealed class SettingsUtilityCommandTests
{
    /// <summary>
    /// Verifies that reports and executable folders are copied independently.
    /// </summary>
    [Fact]
    public void FolderCopyCommandsUseTheirOwnPaths()
    {
        var desktop = new Mock<IDesktopInteractionService>();
        var viewModel = CreateViewModel(desktop.Object);

        viewModel.CopyReportsFolderCommand.Execute(null);
        viewModel.CopyExecutableFolderCommand.Execute(null);

        desktop.Verify(
            service => service.CopyText(viewModel.ReportsFolder),
            Times.Once);
        desktop.Verify(
            service => service.CopyText(viewModel.ExecutableFolder),
            Times.Once);
        viewModel.ReportsFolder.Should().NotBe(viewModel.ExecutableFolder);
    }

    /// <summary>
    /// Verifies that shortcut creation targets the UI executable in the displayed folder.
    /// </summary>
    [Fact]
    public async Task DesktopShortcutTargetsPublishedUiExecutable()
    {
        var desktop = new Mock<IDesktopInteractionService>();
        var viewModel = CreateViewModel(desktop.Object);
        var expectedExecutable = Path.Combine(
            viewModel.ExecutableFolder,
            "OD.MyLocalAgent.UI.exe");
        desktop
            .Setup(
                service => service.CreateDesktopShortcut(
                    "OD.MyLocalAgent",
                    expectedExecutable))
            .Returns(Path.Combine("C:", "Desktop", "OD.MyLocalAgent.lnk"));

        await viewModel.CreateDesktopShortcutCommand.ExecuteAsync(null);

        desktop.VerifyAll();
    }

    /// <summary>
    /// Verifies that staying on Settings preserves the edited state.
    /// </summary>
    [Fact]
    public async Task NavigationCanBeCanceledWithUnsavedChanges()
    {
        var desktop = new Mock<IDesktopInteractionService>();
        desktop.Setup(static service => service.PromptUnsavedChanges(
                "Unsaved settings",
                "Save your changes before leaving Settings?"))
            .Returns(UnsavedChangesChoice.Stay);
        var viewModel = CreateViewModel(desktop.Object);
        viewModel.OllamaModel = "edited-model";

        var canNavigate = await viewModel.CanNavigateAwayAsync();

        canNavigate.Should().BeFalse();
        viewModel.IsDirty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that explicitly abandoning edits permits navigation and clears dirty state.
    /// </summary>
    [Fact]
    public async Task NavigationCanDiscardUnsavedChanges()
    {
        var desktop = new Mock<IDesktopInteractionService>();
        desktop.Setup(static service => service.PromptUnsavedChanges(
                "Unsaved settings",
                "Save your changes before leaving Settings?"))
            .Returns(UnsavedChangesChoice.Discard);
        var viewModel = CreateViewModel(desktop.Object);
        viewModel.OllamaModel = "edited-model";

        var canNavigate = await viewModel.CanNavigateAwayAsync();

        canNavigate.Should().BeTrue();
        viewModel.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Verifies refreshing editable model lists cannot erase their bound text values.
    /// </summary>
    [Fact]
    public async Task ModelCatalogRefreshPreservesEditedModelNames()
    {
        var ollamaCatalog = new Mock<IOllamaModelCatalog>();
        ollamaCatalog
            .Setup(catalog => catalog.ListInstalledModelsAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["ollama-new"]);
        var openAICatalog = new Mock<IOpenAIModelCatalog>();
        openAICatalog
            .Setup(catalog => catalog.ListModelsAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["openai-new"]);
        var viewModel = CreateViewModel(
            Mock.Of<IDesktopInteractionService>(),
            ollamaCatalog.Object,
            openAICatalog.Object);
        viewModel.OllamaModel = "ollama-selected";
        viewModel.OpenAIModel = "openai-selected";
        viewModel.IsDirty = false;
        viewModel.OllamaModels.CollectionChanged += (_, args) =>
        {
            if (args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                viewModel.OllamaModel = string.Empty;
            }
        };
        viewModel.OpenAIModels.CollectionChanged += (_, args) =>
        {
            if (args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                viewModel.OpenAIModel = string.Empty;
            }
        };

        await viewModel.RefreshOllamaModelsCommand.ExecuteAsync(null);
        await viewModel.RefreshOpenAIModelsCommand.ExecuteAsync(null);

        viewModel.OllamaModel.Should().Be("ollama-selected");
        viewModel.OpenAIModel.Should().Be("openai-selected");
        viewModel.IsDirty.Should().BeFalse();
        ollamaCatalog.VerifyAll();
        openAICatalog.VerifyAll();
    }

    private static SettingsViewModel CreateViewModel(
        IDesktopInteractionService desktopInteraction,
        IOllamaModelCatalog? ollamaModelCatalog = null,
        IOpenAIModelCatalog? openAIModelCatalog = null)
    {
        return new SettingsViewModel(
            new MutableOptionsMonitor<AIOptions>(new AIOptions()),
            new MutableOptionsMonitor<OllamaOptions>(new OllamaOptions()),
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions()),
            new MutableOptionsMonitor<FileSystemOptions>(new FileSystemOptions()),
            new MutableOptionsMonitor<HttpOptions>(new HttpOptions()),
            new MutableOptionsMonitor<SchedulerOptions>(new SchedulerOptions()),
            new MutableOptionsMonitor<GuardrailsOptions>(new GuardrailsOptions()),
            new MutableOptionsMonitor<LoggingOptions>(new LoggingOptions()),
            Mock.Of<ILlmClientFactory>(),
            ollamaModelCatalog ?? Mock.Of<IOllamaModelCatalog>(),
            openAIModelCatalog ?? Mock.Of<IOpenAIModelCatalog>(),
            Mock.Of<ISecretStore>(),
            Mock.Of<IUserSettingsService>(),
            Mock.Of<IThemeService>(),
            Mock.Of<IIncidentLogService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IInMemoryLogSink>(),
            Mock.Of<ILogManagementService>(),
            desktopInteraction,
            Mock.Of<IMessenger>(),
            TimeProvider.System);
    }
}
