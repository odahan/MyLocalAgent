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

    private static SettingsViewModel CreateViewModel(
        IDesktopInteractionService desktopInteraction)
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
            Mock.Of<IOllamaModelCatalog>(),
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
