using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Administers and diagnostically searches the embedded Knowledge library.</summary>
public sealed partial class KnowledgeViewModel : ObservableObject, IActivatablePage
{
    private readonly IKnowledgeLibraryService _library;
    private readonly IKnowledgeSearchService _search;
    private readonly IDesktopInteractionService _desktop;

    /// <summary>Initializes the Knowledge page model.</summary>
    public KnowledgeViewModel(
        IKnowledgeLibraryService library,
        IKnowledgeSearchService search,
        IDesktopInteractionService desktop)
    {
        _library = library;
        _search = search;
        _desktop = desktop;
    }

    /// <summary>Gets imported documents.</summary>
    public ObservableCollection<KnowledgeDocument> Documents { get; } = [];
    /// <summary>Gets diagnostic search results.</summary>
    public ObservableCollection<KnowledgeSearchResult> SearchResults { get; } = [];
    /// <summary>Gets bounded active chunk previews for the selected document.</summary>
    public ObservableCollection<KnowledgeChunkPreview> ChunkPreviews { get; } = [];

    [ObservableProperty] private KnowledgeDocument? _selectedDocument;
    [ObservableProperty] private string _importName = string.Empty;
    [ObservableProperty] private string _importDescription = string.Empty;
    [ObservableProperty] private string _importTags = string.Empty;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _searchTags = string.Empty;
    [ObservableProperty] private string _statusText = "Knowledge has not been loaded.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _requiresReindex;
    [ObservableProperty] private string _indexContractText = "The Knowledge index contract has not been loaded.";
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private string _documentFilter = string.Empty;

    /// <summary>Gets documents matching the current name or tag filter.</summary>
    public IEnumerable<KnowledgeDocument> FilteredDocuments => string.IsNullOrWhiteSpace(DocumentFilter)
        ? Documents
        : Documents.Where(document =>
            document.Name.Contains(DocumentFilter.Trim(), StringComparison.OrdinalIgnoreCase) ||
            document.Tags.Any(tag => tag.Contains(DocumentFilter.Trim(), StringComparison.OrdinalIgnoreCase)));

    /// <inheritdoc />
    public void Activate() => ReloadCommand.Execute(null);

    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await _library.InitializeAsync(cancellationToken).ConfigureAwait(true);
            Documents.Clear();
            foreach (var document in await _library.GetDocumentsAsync(cancellationToken).ConfigureAwait(true))
            {
                Documents.Add(document);
            }
            OnPropertyChanged(nameof(FilteredDocuments));
            await RefreshIndexStatusAsync(cancellationToken).ConfigureAwait(true);
            StatusText = $"{Documents.Count} Knowledge document(s).";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ImportAsync(CancellationToken cancellationToken)
    {
        var path = _desktop.SelectOpenFile(
            "Import Knowledge document",
            "Supported documents|*.txt;*.md;*.markdown;*.docx;*.pdf");
        if (path is null)
        {
            return;
        }
        var tags = ParseTags(ImportTags);
        if (tags.Count == 0)
        {
            StatusText = "Enter at least one authorization tag before importing.";
            return;
        }
        IsBusy = true;
        try
        {
            var name = string.IsNullOrWhiteSpace(ImportName) ? Path.GetFileNameWithoutExtension(path) : ImportName;
            KnowledgeDocument document;
            try
            {
                document = await _library.ImportAsync(path, name, ImportDescription, tags, cancellationToken).ConfigureAwait(true);
            }
            catch (KnowledgeDuplicateException duplicate) when (_desktop.Confirm(
                "Import duplicate content separately?",
                $"The same content already exists as '{duplicate.ExistingDocumentName}'. Continue only if you intentionally need a separate administrative document."))
            {
                document = await _library.ImportAsync(path, name, ImportDescription, tags, cancellationToken, allowDuplicate: true).ConfigureAwait(true);
            }

            Documents.Insert(0, document);
            OnPropertyChanged(nameof(FilteredDocuments));
            SelectedDocument = document;
            StatusText = "Document imported and indexed.";
        }
        catch (KnowledgeDuplicateException duplicate)
        {
            StatusText = $"Import canceled. Duplicate content already exists as '{duplicate.ExistingDocumentName}'.";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var document = SelectedDocument;
        if (document is null || !_desktop.Confirm(
            "Delete Knowledge document?",
            "This removes the private library copy and its searchable chunks."))
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (await _library.DeleteAsync(document.Id, cancellationToken).ConfigureAwait(true))
            {
                Documents.Remove(document);
                OnPropertyChanged(nameof(FilteredDocuments));
                SelectedDocument = null;
                StatusText = "Knowledge document deleted.";
            }
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ReplaceAsync(CancellationToken cancellationToken)
    {
        var document = SelectedDocument;
        if (document is null)
        {
            return;
        }

        var path = _desktop.SelectOpenFile(
            "Replace Knowledge document content",
            "Supported documents|*.txt;*.md;*.markdown;*.docx;*.pdf");
        if (path is null)
        {
            return;
        }

        if (!_desktop.Confirm(
            "Replace document content?",
            "The new content is indexed completely before it atomically replaces the current searchable content."))
        {
            return;
        }
        IsBusy = true;
        try
        {
            var replacement = await _library.ReplaceAsync(document.Id, path, cancellationToken).ConfigureAwait(true);
            ReplaceDocument(document, replacement);
            SelectedDocument = replacement;
            StatusText = "Document content replaced and indexed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Document replacement was canceled.";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyDetailsAsync(CancellationToken cancellationToken)
    {
        var document = SelectedDocument;
        if (document is null)
        {
            return;
        }

        var tags = ParseTags(ImportTags);
        if (tags.Count == 0)
        {
            StatusText = "Enter at least one authorization tag.";
            return;
        }
        IsBusy = true;
        try
        {
            var updated = await _library.UpdateDetailsAsync(document.Id, ImportName, ImportDescription, tags, cancellationToken).ConfigureAwait(true);
            ReplaceDocument(document, updated);
            SelectedDocument = updated;
            StatusText = "Document details updated without re-embedding.";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ReindexDocumentAsync(CancellationToken cancellationToken)
    {
        var document = SelectedDocument;
        if (document is null)
        {
            return;
        }
        IsBusy = true;
        try
        {
            var progress = new Progress<KnowledgeIndexProgress>(UpdateProgress);
            var updated = await _library.ReindexDocumentAsync(document.Id, progress, cancellationToken).ConfigureAwait(true);
            ReplaceDocument(document, updated);
            SelectedDocument = updated;
            await RefreshIndexStatusAsync(cancellationToken).ConfigureAwait(true);
            StatusText = "Document reindexed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Document reindex was canceled.";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ReindexLibraryAsync(CancellationToken cancellationToken)
    {
        if (Documents.Count == 0)
        {
            StatusText = "There are no Knowledge documents to reindex.";
            return;
        }

        if (!_desktop.Confirm(
            "Reindex Knowledge library?",
            "All documents will be embedded with the configured model and activated together only after the complete rebuild succeeds."))
        {
            return;
        }
        IsBusy = true;
        try
        {
            await _library.ReindexLibraryAsync(new Progress<KnowledgeIndexProgress>(UpdateProgress), cancellationToken).ConfigureAwait(true);
            await ReloadAsync(cancellationToken).ConfigureAwait(true);
            StatusText = "Knowledge library reindexed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "Library reindex was canceled; the previous active index was preserved.";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestSearchAsync(CancellationToken cancellationToken)
    {
        var tags = ParseTags(SearchTags);
        if (tags.Count == 0)
        {
            StatusText = "Select at least one diagnostic search tag.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _search.SearchAsync(
                SearchQuery,
                tags,
                null,
                null,
                cancellationToken).ConfigureAwait(true);
            SearchResults.Clear();
            foreach (var item in result.Results)
            {
                SearchResults.Add(item);
            }

            StatusText = $"Search returned {result.ResultCount} result(s).";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadChunkPreviewAsync(CancellationToken cancellationToken)
    {
        var document = SelectedDocument;
        if (document is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var previews = await _library.GetChunkPreviewAsync(document.Id, 20, cancellationToken).ConfigureAwait(true);
            ChunkPreviews.Clear();
            foreach (var preview in previews)
            {
                ChunkPreviews.Add(preview);
            }
            StatusText = $"Loaded {previews.Count} bounded chunk preview(s).";
        }
        catch (Exception exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static IReadOnlyList<string> ParseTags(string value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    partial void OnDocumentFilterChanged(string value) => OnPropertyChanged(nameof(FilteredDocuments));

    partial void OnSelectedDocumentChanged(KnowledgeDocument? value)
    {
        ChunkPreviews.Clear();
        if (value is null)
        {
            return;
        }

        ImportName = value.Name;
        ImportDescription = value.Description;
        ImportTags = string.Join(", ", value.Tags);
    }

    private async Task RefreshIndexStatusAsync(CancellationToken cancellationToken)
    {
        var status = await _library.GetIndexStatusAsync(cancellationToken).ConfigureAwait(true);
        RequiresReindex = status.RequiresReindex;
        IndexContractText = !status.HasIndex
            ? $"Configured: {status.ConfiguredContract}. The first import establishes the index."
            : status.RequiresReindex
                ? $"Reindex required. Configured: {status.ConfiguredContract}. Active: {status.IndexedContract}."
                : $"Active index: {status.IndexedContract}.";
    }

    private void UpdateProgress(KnowledgeIndexProgress value) => ProgressText = $"{value.Phase}: {value.CompletedDocuments}/{value.TotalDocuments} documents, {value.CompletedEmbeddings}/{value.TotalEmbeddings} embeddings";

    private void ReplaceDocument(KnowledgeDocument previous, KnowledgeDocument replacement)
    {
        var index = Documents.IndexOf(previous);
        if (index >= 0)
        {
            Documents[index] = replacement;
        }
        OnPropertyChanged(nameof(FilteredDocuments));
    }
}
