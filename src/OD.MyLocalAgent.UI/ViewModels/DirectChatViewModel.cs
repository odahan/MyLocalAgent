using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>Coordinates persisted conversations, memory compression, and streamed replies.</summary>
public sealed partial class DirectChatViewModel : ObservableObject, IActivatablePage, IAsyncNavigationGuard
{
    private const int MessagePageSize = 100;
    private readonly IDirectChatStore _store;
    private readonly IDirectChatContextBuilder _contextBuilder;
    private readonly IDirectChatHistoryCompressor _compressor;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILlmClientFactory _llmClientFactory;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMarkdownRenderingService _markdown;
    private readonly bool _isMarkdownAvailable;
    private bool _loaded;
    private bool _selectingConversation;

    /// <summary>Initializes the Direct Chat surface.</summary>
    public DirectChatViewModel(
        IDirectChatStore store,
        IDirectChatContextBuilder contextBuilder,
        IDirectChatHistoryCompressor compressor,
        ITokenEstimator tokenEstimator,
        ILlmClientFactory llmClientFactory,
        IOptionsMonitor<AIOptions> aiOptions,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IMarkdownRenderingService markdown,
        IWebView2AvailabilityService webViewAvailability)
    {
        _store = store;
        _contextBuilder = contextBuilder;
        _compressor = compressor;
        _tokenEstimator = tokenEstimator;
        _llmClientFactory = llmClientFactory;
        _aiOptions = aiOptions;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _markdown = markdown;
        _isMarkdownAvailable = webViewAvailability.IsAvailable();
    }

    /// <summary>Gets conversations ordered by most recent activity.</summary>
    public ObservableCollection<DirectChatConversationItem> Conversations { get; } = [];

    /// <summary>Gets the messages currently displayed.</summary>
    public ObservableCollection<DirectChatMessageItem> Messages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteConversationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private DirectChatConversationItem? _selectedConversation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _draftMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewConversationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteConversationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewConversationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteConversationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isCompressing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOlderMessagesCommand))]
    private bool _hasOlderMessages;

    [ObservableProperty]
    private string _statusText = "Direct Chat has not been loaded.";

    [ObservableProperty]
    private string _providerBadge = "Provider not checked";

    /// <inheritdoc />
    public void Activate()
    {
        ProviderBadge = BuildProviderBadge();
        if (!_loaded && !LoadCommand.IsRunning)
        {
            LoadCommand.Execute(null);
        }
    }

    /// <inheritdoc />
    public Task<bool> CanNavigateAwayAsync()
    {
        if (!IsStreaming)
        {
            return Task.FromResult(true);
        }

        var leave = _desktopInteraction.Confirm(
            "Leave Direct Chat",
            "A response is still being generated. Cancel it and leave Direct Chat?");
        if (leave)
        {
            SendCancelCommand.Execute(null);
        }

        return Task.FromResult(leave);
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(true);
            var conversations = await _store.GetConversationsAsync(cancellationToken)
                .ConfigureAwait(true);
            Conversations.Clear();
            foreach (var conversation in conversations)
            {
                Conversations.Add(new DirectChatConversationItem(conversation));
            }

            _loaded = true;
            if (Conversations.Count == 0)
            {
                await CreateAndSelectConversationAsync(cancellationToken).ConfigureAwait(true);
            }
            else
            {
                SelectConversationWithoutAutomaticLoad(Conversations[0]);
                await LoadSelectedConversationAsync(cancellationToken).ConfigureAwait(true);
            }

            StatusText = "Ready.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(exception, "Direct Chat load").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanMutateConversation))]
    private async Task NewConversationAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await CreateAndSelectConversationAsync(cancellationToken).ConfigureAwait(true);
            StatusText = "New conversation created.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(exception, "Direct Chat new conversation")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteConversation))]
    private async Task DeleteConversationAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        if (selected is null ||
            !_desktopInteraction.Confirm(
                "Delete conversation",
                $"Delete '{selected.Title}' and all of its messages?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            _ = await _store.DeleteConversationAsync(selected.Id, cancellationToken)
                .ConfigureAwait(true);
            Conversations.Remove(selected);
            if (Conversations.Count == 0)
            {
                await CreateAndSelectConversationAsync(cancellationToken).ConfigureAwait(true);
            }
            else
            {
                SelectConversationWithoutAutomaticLoad(Conversations[0]);
                await LoadSelectedConversationAsync(cancellationToken).ConfigureAwait(true);
            }

            StatusText = "Conversation deleted.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(exception, "Direct Chat delete conversation")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadOlderMessages))]
    private async Task LoadOlderMessagesAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        var firstSequence = Messages.FirstOrDefault()?.Sequence;
        if (selected is null || firstSequence is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var older = await _store.GetMessagesAsync(
                    selected.Id,
                    firstSequence,
                    MessagePageSize,
                    cancellationToken)
                .ConfigureAwait(true);
            for (var index = older.Count - 1; index >= 0; index--)
            {
                Messages.Insert(0, CreateMessageItem(older[index]));
            }

            HasOlderMessages = older.Count == MessagePageSize;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(exception, "Direct Chat older messages")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        var text = DraftMessage.Trim();
        if (selected is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsStreaming = true;
        DirectChatMessage? assistantMessage = null;
        DirectChatMessageItem? assistantItem = null;
        try
        {
            DraftMessage = string.Empty;
            var userMessage = await _store.AppendMessageAsync(
                    selected.Id,
                    DirectChatMessageRole.User,
                    text,
                    DirectChatMessageState.Completed,
                    _tokenEstimator.EstimateText(text),
                    cancellationToken)
                .ConfigureAwait(true);
            Messages.Add(CreateMessageItem(userMessage));

            if (string.Equals(selected.Title, "New chat", StringComparison.Ordinal))
            {
                await _store.UpdateConversationTitleAsync(
                        selected.Id,
                        CreateTitle(text),
                        cancellationToken)
                    .ConfigureAwait(true);
                selected.Title = CreateTitle(text);
            }

            var conversation = await RequireConversationAsync(selected.Id, cancellationToken)
                .ConfigureAwait(true);
            var allMessages = await GetAllMessagesAsync(selected.Id, cancellationToken)
                .ConfigureAwait(true);
            var plan = _contextBuilder.Build(conversation, allMessages);
            if (plan.RequiresCompression)
            {
                IsCompressing = true;
                StatusText = "Compressing conversation history…";
                try
                {
                    try
                    {
                        if (await _compressor.CompressAsync(
                                conversation,
                                allMessages,
                                cancellationToken)
                            .ConfigureAwait(true))
                        {
                            conversation = await RequireConversationAsync(
                                    selected.Id,
                                    cancellationToken)
                                .ConfigureAwait(true);
                            plan = _contextBuilder.Build(conversation, allMessages);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception compressionException) when (plan.FitsWithResponseReserve)
                    {
                        _ = await _incidentReporter.ReportAsync(
                                compressionException,
                                "Direct Chat history compression",
                                CancellationToken.None)
                            .ConfigureAwait(true);
                        StatusText = "History compression failed; continuing with the current context.";
                    }
                }
                finally
                {
                    IsCompressing = false;
                }
            }

            if (!plan.FitsWithResponseReserve)
            {
                throw new InvalidOperationException(
                    "The conversation is too large for the configured model context window. Start a new conversation or increase the declared context window.");
            }

            assistantMessage = await _store.AppendMessageAsync(
                    selected.Id,
                    DirectChatMessageRole.Assistant,
                    string.Empty,
                    DirectChatMessageState.Streaming,
                    null,
                    cancellationToken)
                .ConfigureAwait(true);
            assistantItem = CreateMessageItem(assistantMessage);
            Messages.Add(assistantItem);
            StatusText = "Generating response…";

            var provider = _llmClientFactory.Create();
            ProviderBadge = $"{provider.ProviderName} · {provider.ModelName}";
            var policy = GenerationPolicyResolver.Resolve(
                _aiOptions.CurrentValue.GenerationDefaults);
            var client = new GenerationPolicyChatClient(
                provider.ChatClient,
                policy,
                provider.ProviderName,
                "direct-chat");
            var response = new System.Text.StringBuilder();
            var checkpoint = Stopwatch.StartNew();
            await foreach (var update in client
                               .GetStreamingResponseAsync(
                                   plan.Messages,
                                   cancellationToken: cancellationToken)
                               .ConfigureAwait(true))
            {
                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                response.Append(update.Text);
                assistantItem.Update(response.ToString(), DirectChatMessageState.Streaming);
                if (checkpoint.Elapsed >= TimeSpan.FromSeconds(1))
                {
                    await _store.UpdateMessageAsync(
                            assistantMessage.Id,
                            response.ToString(),
                            DirectChatMessageState.Streaming,
                            _tokenEstimator.EstimateText(response.ToString()),
                            cancellationToken)
                        .ConfigureAwait(true);
                    checkpoint.Restart();
                }
            }

            assistantItem.Update(response.ToString(), DirectChatMessageState.Completed);
            await _store.UpdateMessageAsync(
                    assistantMessage.Id,
                    response.ToString(),
                    DirectChatMessageState.Completed,
                    _tokenEstimator.EstimateText(response.ToString()),
                    cancellationToken)
                .ConfigureAwait(true);
            await RefreshSelectedConversationMetadataAsync(cancellationToken)
                .ConfigureAwait(true);
            StatusText = "Response completed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (assistantMessage is not null && assistantItem is not null)
            {
                assistantItem.Update(assistantItem.Content, DirectChatMessageState.Canceled);
                await TryFinalizeMessageAsync(
                        assistantMessage.Id,
                        assistantItem.Content,
                        DirectChatMessageState.Canceled)
                    .ConfigureAwait(true);
            }

            StatusText = "Response canceled.";
        }
        catch (Exception exception)
        {
            if (assistantMessage is not null && assistantItem is not null)
            {
                assistantItem.Update(assistantItem.Content, DirectChatMessageState.Failed);
                await TryFinalizeMessageAsync(
                        assistantMessage.Id,
                        assistantItem.Content,
                        DirectChatMessageState.Failed)
                    .ConfigureAwait(true);
            }

            await HandleExceptionAsync(exception, "Direct Chat generation")
                .ConfigureAwait(true);
        }
        finally
        {
            IsCompressing = false;
            IsStreaming = false;
        }
    }

    partial void OnSelectedConversationChanged(DirectChatConversationItem? value)
    {
        if (value is null || _selectingConversation || IsStreaming)
        {
            return;
        }

        _ = LoadSelectedConversationSafelyAsync();
    }

    private bool CanMutateConversation() => !IsBusy && !IsStreaming;

    private bool CanDeleteConversation() =>
        CanMutateConversation() && SelectedConversation is not null;

    private bool CanSend() =>
        !IsBusy &&
        !IsStreaming &&
        SelectedConversation is not null &&
        !string.IsNullOrWhiteSpace(DraftMessage);

    private bool CanLoadOlderMessages() =>
        HasOlderMessages && !IsBusy && !IsStreaming;

    private async Task CreateAndSelectConversationAsync(
        CancellationToken cancellationToken)
    {
        var conversation = await _store.CreateConversationAsync(
                "New chat",
                cancellationToken)
            .ConfigureAwait(true);
        var item = new DirectChatConversationItem(conversation);
        Conversations.Insert(0, item);
        SelectConversationWithoutAutomaticLoad(item);

        Messages.Clear();
        HasOlderMessages = false;
    }

    private async Task LoadSelectedConversationSafelyAsync()
    {
        try
        {
            await LoadSelectedConversationAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(exception, "Direct Chat conversation selection")
                .ConfigureAwait(true);
        }
    }

    private void SelectConversationWithoutAutomaticLoad(
        DirectChatConversationItem item)
    {
        _selectingConversation = true;
        try
        {
            SelectedConversation = item;
        }
        finally
        {
            _selectingConversation = false;
        }
    }

    private async Task LoadSelectedConversationAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        if (selected is null)
        {
            Messages.Clear();
            return;
        }

        var messages = await _store.GetMessagesAsync(
                selected.Id,
                null,
                MessagePageSize,
                cancellationToken)
            .ConfigureAwait(true);
        if (!string.Equals(SelectedConversation?.Id, selected.Id, StringComparison.Ordinal))
        {
            return;
        }

        Messages.Clear();
        foreach (var message in messages)
        {
            Messages.Add(CreateMessageItem(message));
        }

        HasOlderMessages = messages.Count == MessagePageSize;
    }

    private async Task<IReadOnlyList<DirectChatMessage>> GetAllMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        var all = new List<DirectChatMessage>();
        long? before = null;
        while (true)
        {
            var page = await _store.GetMessagesAsync(
                    conversationId,
                    before,
                    1000,
                    cancellationToken)
                .ConfigureAwait(true);
            if (page.Count == 0)
            {
                break;
            }

            all.InsertRange(0, page);
            if (page.Count < 1000)
            {
                break;
            }

            before = page[0].Sequence;
        }

        return all;
    }

    private async Task<DirectChatConversation> RequireConversationAsync(
        string conversationId,
        CancellationToken cancellationToken) =>
        await _store.GetConversationAsync(conversationId, cancellationToken)
            .ConfigureAwait(true) ??
        throw new InvalidOperationException("The Direct Chat conversation no longer exists.");

    private async Task RefreshSelectedConversationMetadataAsync(
        CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        if (selected is null)
        {
            return;
        }

        selected.Update(await RequireConversationAsync(selected.Id, cancellationToken)
            .ConfigureAwait(true));
        Conversations.Move(Conversations.IndexOf(selected), 0);
    }

    private async Task TryFinalizeMessageAsync(
        string messageId,
        string content,
        DirectChatMessageState state)
    {
        try
        {
            await _store.UpdateMessageAsync(
                    messageId,
                    content,
                    state,
                    string.IsNullOrEmpty(content) ? null : _tokenEstimator.EstimateText(content),
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch
        {
            // Startup recovery marks any remaining streaming record as failed.
        }
    }

    private DirectChatMessageItem CreateMessageItem(DirectChatMessage message) =>
        new(message, _markdown, _isMarkdownAvailable);

    private static string CreateTitle(string firstMessage)
    {
        var normalized = string.Join(
            ' ',
            firstMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 60 ? normalized : normalized[..57] + "…";
    }

    private string BuildProviderBadge()
    {
        try
        {
            var provider = _llmClientFactory.Create();
            return $"{provider.ProviderName} · {provider.ModelName}";
        }
        catch
        {
            return "Provider not configured";
        }
    }

    private async Task HandleExceptionAsync(Exception exception, string operation)
    {
        StatusText = exception.Message;
        _ = await _incidentReporter.ReportAsync(
                exception,
                operation,
                CancellationToken.None)
            .ConfigureAwait(true);
    }
}
