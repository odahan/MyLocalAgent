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
        viewModel.ExistingTags.Should().Equal("maui");
        viewModel.DocumentFilterTag = "MAUI";
        viewModel.FilteredDocuments.Should().ContainSingle();
        viewModel.DocumentFilterTag = "mau";
        viewModel.FilteredDocuments.Should().BeEmpty();
        viewModel.ClearDocumentFilterCommand.Execute(null);
        viewModel.FilteredDocuments.Should().ContainSingle();
        viewModel.ImportName = "Next import";
        viewModel.ImportDescription = "Independent import metadata";
        viewModel.ImportTags = "book, french";
        viewModel.SelectedExistingTag = "maui";
        viewModel.AddSelectedExistingTagCommand.Execute(null);
        viewModel.AddSelectedExistingTagCommand.Execute(null);
        viewModel.ImportTags.Should().Be("book, french, maui");
        viewModel.SelectedDocument = document;
        viewModel.ImportName.Should().Be("Next import");
        viewModel.ImportDescription.Should().Be("Independent import metadata");
        viewModel.ImportTags.Should().Be("book, french, maui");
        viewModel.LoadChunkPreviewCommand.Execute(null);
        await (viewModel.LoadChunkPreviewCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.ChunkPreviews.Should().ContainSingle().Which.PageNumber.Should().Be(2);
        viewModel.SearchQuery = "startup";
        viewModel.SearchTag1 = "maui";
        viewModel.SearchTag2 = "maui";
        viewModel.TestSearchCommand.Execute(null);
        await (viewModel.TestSearchCommand.ExecutionTask ?? Task.CompletedTask);
        viewModel.SearchStatusText.Should().Be("Search returned 0 result(s).");
        viewModel.ClearSearchTagsCommand.Execute(null);
        viewModel.SearchTag1.Should().BeNull();
        viewModel.SearchTag2.Should().BeNull();
        library.VerifyAll();
        search.VerifyAll();
    }

    /// <summary>Verifies existing metadata is edited through the dedicated dialog contract.</summary>
    [Fact]
    public async Task EditMetadataUsesDedicatedDialogAndUpdatesSelection()
    {
        var now = DateTimeOffset.Parse("2026-08-20T10:00:00Z");
        var document = new KnowledgeDocument(
            "document",
            "Original",
            "Old description",
            ["old"],
            "guide.pdf",
            "application/pdf",
            100,
            50,
            1,
            now);
        var updated = document with
        {
            Name = "Updated",
            Description = "New description",
            Tags = ["book", "french"],
        };
        var library = new Mock<IKnowledgeLibraryService>(MockBehavior.Strict);
        library.Setup(item => item.UpdateDetailsAsync(
                document.Id,
                updated.Name,
                updated.Description,
                It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(updated.Tags)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);
        var editor = new Mock<IKnowledgeMetadataEditorDialogService>(MockBehavior.Strict);
        editor.Setup(item => item.Show(It.Is<KnowledgeMetadataEditorRequest>(request =>
                request.Name == document.Name &&
                request.Description == document.Description &&
                request.Tags.SequenceEqual(document.Tags))))
            .Returns(new KnowledgeMetadataEditorResult(
                updated.Name,
                updated.Description,
                updated.Tags));
        var viewModel = new KnowledgeViewModel(
            library.Object,
            Mock.Of<IKnowledgeSearchService>(),
            Mock.Of<IDesktopInteractionService>(),
            editor.Object);
        viewModel.Documents.Add(document);
        viewModel.SelectedDocument = document;

        viewModel.EditMetadataCommand.Execute(null);
        await (viewModel.EditMetadataCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.SelectedDocument.Should().Be(updated);
        viewModel.Documents.Should().ContainSingle().Which.Should().Be(updated);
        viewModel.DocumentsStatusText.Should().Be("Document details updated without re-embedding.");
        library.VerifyAll();
        editor.VerifyAll();
    }

    /// <summary>Verifies deleting a document clears stale search criteria and results.</summary>
    [Fact]
    public async Task DeleteClearsSearchState()
    {
        var document = new KnowledgeDocument(
            "document",
            "Electronics",
            "French electronics book",
            ["book", "french"],
            "electronics.pdf",
            "application/pdf",
            100,
            50,
            1,
            DateTimeOffset.Parse("2026-08-20T10:00:00Z"));
        var library = new Mock<IKnowledgeLibraryService>(MockBehavior.Strict);
        library.Setup(item => item.DeleteAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var desktop = new Mock<IDesktopInteractionService>(MockBehavior.Strict);
        desktop.Setup(item => item.Confirm(
                "Delete Knowledge document?",
                "This removes the private library copy and its searchable chunks."))
            .Returns(true);
        var viewModel = new KnowledgeViewModel(
            library.Object,
            Mock.Of<IKnowledgeSearchService>(),
            desktop.Object);
        viewModel.Documents.Add(document);
        viewModel.SelectedDocument = document;
        viewModel.SearchQuery = "condensateur";
        viewModel.SearchTag1 = "book";
        viewModel.SearchTag2 = "french";
        viewModel.SearchResults.Add(new KnowledgeSearchResult(
            1,
            0.9f,
            document.Id,
            document.Name,
            document.Description,
            document.Tags,
            "chunk",
            1,
            "Un condensateur stocke de l'énergie.",
            "Electronics, page 1",
            null,
            1));

        viewModel.DeleteCommand.Execute(null);
        await (viewModel.DeleteCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.Documents.Should().BeEmpty();
        viewModel.SelectedDocument.Should().BeNull();
        viewModel.SearchQuery.Should().BeEmpty();
        viewModel.SearchTag1.Should().BeNull();
        viewModel.SearchTag2.Should().BeNull();
        viewModel.SearchTag3.Should().BeNull();
        viewModel.SearchTag4.Should().BeNull();
        viewModel.SearchResults.Should().BeEmpty();
        viewModel.SearchStatusText.Should().BeEmpty();
        viewModel.DocumentsStatusText.Should().Be("Knowledge document deleted.");
        library.VerifyAll();
        desktop.VerifyAll();
    }
}
