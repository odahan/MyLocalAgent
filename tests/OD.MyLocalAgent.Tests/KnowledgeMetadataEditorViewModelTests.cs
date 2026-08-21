using FluentAssertions;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the memory-only Knowledge metadata editor session.</summary>
public sealed class KnowledgeMetadataEditorViewModelTests
{
    /// <summary>Verifies unchanged sessions cannot apply and edited tags are normalized.</summary>
    [Fact]
    public void ApplyRequiresChangesAndReturnsValidatedMetadata()
    {
        var viewModel = new KnowledgeMetadataEditorViewModel();
        viewModel.Initialize(new KnowledgeMetadataEditorRequest(
            "Guide",
            "Description",
            ["french"]));

        viewModel.HasChanges.Should().BeFalse();
        viewModel.ApplyCommand.CanExecute(null).Should().BeFalse();

        viewModel.Name = "Updated guide";
        viewModel.TagsText = "Book,  FRENCH";
        viewModel.ApplyCommand.CanExecute(null).Should().BeTrue();
        viewModel.ApplyCommand.Execute(null);

        viewModel.Result.Should().BeEquivalentTo(new KnowledgeMetadataEditorResult(
            "Updated guide",
            "Description",
            ["book", "french"]));
    }

    /// <summary>Verifies invalid or duplicate authorization tags cannot be applied.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("book, BOOK")]
    [InlineData("*")]
    public void InvalidTagsDisableApply(string tags)
    {
        var viewModel = new KnowledgeMetadataEditorViewModel();
        viewModel.Initialize(new KnowledgeMetadataEditorRequest(
            "Guide",
            "Description",
            ["french"]));

        viewModel.TagsText = tags;

        viewModel.ApplyCommand.CanExecute(null).Should().BeFalse();
        viewModel.Result.Should().BeNull();
    }
}
