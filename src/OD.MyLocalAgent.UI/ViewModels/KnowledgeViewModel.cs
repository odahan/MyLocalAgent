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
    private readonly IKnowledgeMetadataEditorDialogService? _metadataEditor;

    /// <summary>Initializes the Knowledge page model.</summary>
    public KnowledgeViewModel(
        IKnowledgeLibraryService library,
        IKnowledgeSearchService search,
        IDesktopInteractionService desktop,
        IKnowledgeMetadataEditorDialogService? metadataEditor = null)
    {
        _library = library;
        _search = search;
        _desktop = desktop;
        _metadataEditor = metadataEditor;
    }

    /// <summary>Gets imported documents.</summary>
    public ObservableCollection<KnowledgeDocument> Documents { get; } = [];
    /// <summary>Gets diagnostic search results.</summary>
    public ObservableCollection<KnowledgeSearchResult> SearchResults { get; } = [];
    /// <summary>Gets bounded active chunk previews for the selected document.</summary>
    public ObservableCollection<KnowledgeChunkPreview> ChunkPreviews { get; } = [];
    /// <summary>Gets the normalized tags already used by documents in the library.</summary>
    public ObservableCollection<string> ExistingTags { get; } = [];

    [ObservableProperty] private KnowledgeDocument? _selectedDocument;
    [ObservableProperty] private string _importName = string.Empty;
    [ObservableProperty] private string _importDescription = string.Empty;
    [ObservableProperty] private string _importTags = string.Empty;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string? _searchTag1;
    [ObservableProperty] private string? _searchTag2;
    [ObservableProperty] private string? _searchTag3;
    [ObservableProperty] private string? _searchTag4;
    [ObservableProperty] private string _searchStatusText = string.Empty;
    [ObservableProperty] private string _documentsStatusText = "Knowledge has not been loaded.";
    [ObservableProperty] private string _documentProgressText = string.Empty;
    [ObservableProperty] private string _importStatusText = string.Empty;
    [ObservableProperty] private string _previewStatusText = string.Empty;
    [ObservableProperty] private string _maintenanceStatusText = string.Empty;
    [ObservableProperty] private string _maintenanceProgressText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _requiresReindex;
    [ObservableProperty] private string _indexContractText = "The Knowledge index contract has not been loaded.";
    [ObservableProperty] private string? _documentFilterTag;
    [ObservableProperty] private string? _selectedExistingTag;

    /// <summary>Gets documents matching the current name or tag filter.</summary>
    public IEnumerable<KnowledgeDocument> FilteredDocuments => string.IsNullOrWhiteSpace(DocumentFilterTag)
        ? Documents
        : Documents.Where(document => document.Tags.Contains(DocumentFilterTag, StringComparer.OrdinalIgnoreCase));

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
            RefreshExistingTags();
            OnPropertyChanged(nameof(FilteredDocuments));
            await RefreshIndexStatusAsync(cancellationToken).ConfigureAwait(true);
            DocumentsStatusText = $"{Documents.Count} Knowledge document(s).";
        }
        catch (Exception exception)
        {
            DocumentsStatusText = exception.Message;
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
            ImportStatusText = "Enter at least one authorization tag before importing.";
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
            RefreshExistingTags();
            OnPropertyChanged(nameof(FilteredDocuments));
            SelectedDocument = document;
            ImportName = string.Empty;
            ImportDescription = string.Empty;
            ImportTags = string.Empty;
            ImportStatusText = "Document imported and indexed.";
        }
        catch (KnowledgeDuplicateException duplicate)
        {
            ImportStatusText = $"Import canceled. Duplicate content already exists as '{duplicate.ExistingDocumentName}'.";
        }
        catch (Exception exception)
        {
            ImportStatusText = exception.Message;
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
                RefreshExistingTags();
                OnPropertyChanged(nameof(FilteredDocuments));
                SelectedDocument = null;
                ResetSearch();
                DocumentsStatusText = "Knowledge document deleted.";
            }
        }
        catch (Exception exception)
        {
            DocumentsStatusText = exception.Message;
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
            DocumentsStatusText = "Document content replaced and indexed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DocumentsStatusText = "Document replacement was canceled.";
        }
        catch (Exception exception)
        {
            DocumentsStatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EditMetadataAsync(CancellationToken cancellationToken)
    {
        var document = SelectedDocument;
        if (document is null)
        {
            return;
        }

        if (_metadataEditor is null)
        {
            DocumentsStatusText = "The document metadata editor is unavailable.";
            return;
        }

        var result = _metadataEditor.Show(
            new KnowledgeMetadataEditorRequest(
                document.Name,
                document.Description,
                document.Tags));
        if (result is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = await _library.UpdateDetailsAsync(
                document.Id,
                result.Name,
                result.Description,
                result.Tags,
                cancellationToken).ConfigureAwait(true);
            ReplaceDocument(document, updated);
            RefreshExistingTags();
            SelectedDocument = updated;
            DocumentsStatusText = "Document details updated without re-embedding.";
        }
        catch (Exception exception)
        {
            DocumentsStatusText = exception.Message;
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
            var progress = new Progress<KnowledgeIndexProgress>(UpdateDocumentProgress);
            var updated = await _library.ReindexDocumentAsync(document.Id, progress, cancellationToken).ConfigureAwait(true);
            ReplaceDocument(document, updated);
            SelectedDocument = updated;
            await RefreshIndexStatusAsync(cancellationToken).ConfigureAwait(true);
            DocumentsStatusText = "Document reindexed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DocumentsStatusText = "Document reindex was canceled.";
        }
        catch (Exception exception)
        {
            DocumentsStatusText = exception.Message;
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
            MaintenanceStatusText = "There are no Knowledge documents to reindex.";
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
            await _library.ReindexLibraryAsync(new Progress<KnowledgeIndexProgress>(UpdateMaintenanceProgress), cancellationToken).ConfigureAwait(true);
            await ReloadAsync(cancellationToken).ConfigureAwait(true);
            MaintenanceStatusText = "Knowledge library reindexed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MaintenanceStatusText = "Library reindex was canceled; the previous active index was preserved.";
        }
        catch (Exception exception)
        {
            MaintenanceStatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestSearchAsync(CancellationToken cancellationToken)
    {
        SearchResults.Clear();
        SearchStatusText = string.Empty;
        var tags = GetSelectedSearchTags();
        if (tags.Count == 0)
        {
            SearchStatusText = "Select at least one diagnostic search tag.";
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

            SearchStatusText = $"Hybrid search returned {result.ResultCount} result(s).";
        }
        catch (Exception exception)
        {
            SearchStatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearSearchTags()
    {
        SearchTag1 = null;
        SearchTag2 = null;
        SearchTag3 = null;
        SearchTag4 = null;
    }

    [RelayCommand]
    private void ClearDocumentFilter() => DocumentFilterTag = null;

    private void ResetSearch()
    {
        SearchQuery = string.Empty;
        ClearSearchTags();
        SearchResults.Clear();
        SearchStatusText = string.Empty;
    }

    [RelayCommand]
    private void AddSelectedExistingTag()
    {
        var selected = SelectedExistingTag;
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        var tags = ParseTags(ImportTags).ToList();
        if (tags.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        tags.Add(selected);
        ImportTags = string.Join(", ", tags);
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
            PreviewStatusText = $"Loaded {previews.Count} bounded chunk preview(s).";
        }
        catch (Exception exception)
        {
            PreviewStatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static IReadOnlyList<string> ParseTags(string value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    partial void OnDocumentFilterTagChanged(string? value) => OnPropertyChanged(nameof(FilteredDocuments));

    partial void OnSelectedDocumentChanged(KnowledgeDocument? value)
    {
        ChunkPreviews.Clear();
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

    private void UpdateDocumentProgress(KnowledgeIndexProgress value) => DocumentProgressText = FormatProgress(value);

    private void UpdateMaintenanceProgress(KnowledgeIndexProgress value) => MaintenanceProgressText = FormatProgress(value);

    private static string FormatProgress(KnowledgeIndexProgress value) => $"{value.Phase}: {value.CompletedDocuments}/{value.TotalDocuments} documents, {value.CompletedEmbeddings}/{value.TotalEmbeddings} embeddings";

    private void ReplaceDocument(KnowledgeDocument previous, KnowledgeDocument replacement)
    {
        var index = Documents.IndexOf(previous);
        if (index >= 0)
        {
            Documents[index] = replacement;
        }
        OnPropertyChanged(nameof(FilteredDocuments));
    }

    private void RefreshExistingTags()
    {
        var selected = SelectedExistingTag;
        var documentFilterTag = DocumentFilterTag;
        var searchTag1 = SearchTag1;
        var searchTag2 = SearchTag2;
        var searchTag3 = SearchTag3;
        var searchTag4 = SearchTag4;
        ExistingTags.Clear();
        foreach (var tag in Documents
                     .SelectMany(static document => document.Tags)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            ExistingTags.Add(tag);
        }

        SelectedExistingTag = selected is not null && ExistingTags.Contains(selected, StringComparer.OrdinalIgnoreCase)
            ? ExistingTags.First(tag => string.Equals(tag, selected, StringComparison.OrdinalIgnoreCase))
            : ExistingTags.FirstOrDefault();
        SearchTag1 = ResolveExistingTag(searchTag1);
        SearchTag2 = ResolveExistingTag(searchTag2);
        SearchTag3 = ResolveExistingTag(searchTag3);
        SearchTag4 = ResolveExistingTag(searchTag4);
        DocumentFilterTag = ResolveExistingTag(documentFilterTag);
    }

    private IReadOnlyList<string> GetSelectedSearchTags() =>
        new[] { SearchTag1, SearchTag2, SearchTag3, SearchTag4 }
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private string? ResolveExistingTag(string? tag) =>
        tag is not null && ExistingTags.Contains(tag, StringComparer.OrdinalIgnoreCase)
            ? ExistingTags.First(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase))
            : null;
}
