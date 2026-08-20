using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the Knowledge administration ViewModel delegates bounded service work.</summary>
public sealed class KnowledgeViewModelTests
{
    /// <summary>Verifies reload, name/tag filtering, preview, and diagnostic search delegation.</summary>
    [Fact]
    public async Task AdministrationCommandsUseKnowledgeServices()
    {
        var now = DateTimeOffset.Parse("2026-08-20T10:00:00Z");
        var document = new KnowledgeDocument(
            "document",
            "Architecture guide",
            "Guidance",
            ["maui"],
            "guide.pdf",
            "application/pdf",
            100,
            50,
            1,
            now,
            PageCount: 2);
        var library = new Mock<IKnowledgeLibraryService>(MockBehavior.Strict);
        library.Setup(item => item.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        library.Setup(item => item.GetDocumentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([document]);
        library.Setup(item => item.GetIndexStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new KnowledgeIndexStatus(true, false, "configured", "active", 768));
        library.Setup(item => item.GetChunkPreviewAsync(document.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new KnowledgeChunkPreview(0, "Preview", 2, "Heading", 2)]);
        var search = new Mock<IKnowledgeSearchService>(MockBehavior.Strict);
        search.Setup(item => item.SearchAsync("startup", It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(new[] { "maui" })), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeSearchResponse("ok", ["maui"], []));
        var viewModel = new KnowledgeViewModel(
            library.Object,
            search.Object,
            Mock.Of<IDesktopInteractionService>());

        viewModel.Activate();
        await (viewModel.ReloadCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.DocumentFilter = "MAUI";
        viewModel.FilteredDocuments.Should().ContainSingle();
        viewModel.DocumentFilter = "missing";
        viewModel.FilteredDocuments.Should().BeEmpty();
        viewModel.SelectedDocument = document;
        viewModel.LoadChunkPreviewCommand.Execute(null);
        await (viewModel.LoadChunkPreviewCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.ChunkPreviews.Should().ContainSingle().Which.PageNumber.Should().Be(2);
        viewModel.SearchQuery = "startup";
        viewModel.SearchTags = "maui";
        viewModel.TestSearchCommand.Execute(null);
        await (viewModel.TestSearchCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.StatusText.Should().Be("Search returned 0 result(s).");
        library.VerifyAll();
        search.VerifyAll();
    }
}
