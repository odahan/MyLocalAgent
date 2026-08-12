using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.UI.Messages;
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
    private readonly IMyLocalAgentService _agentService;
    private readonly ITaskStore _taskStore;
    private readonly IMessenger _messenger;
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
        IMyLocalAgentService agentService,
        ITaskStore taskStore,
        IMessenger messenger,
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
        _agentService = agentService;
        _taskStore = taskStore;
        _messenger = messenger;
        _aiOptions = aiOptions;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _markdown = markdown;
        _isMarkdownAvailable = webViewAvailability.IsAvailable();
        foreach (var selection in ToolSelections)
            selection.PropertyChanged += (_, _) => RefreshAgentSummary();
        foreach (var selection in CapabilitySelections)
            selection.PropertyChanged += (_, _) => RefreshAgentSummary();
    }

    /// <summary>Gets conversations ordered by most recent activity.</summary>
    public ObservableCollection<DirectChatConversationItem> Conversations { get; } = [];

    /// <summary>Gets the messages currently displayed.</summary>
    public ObservableCollection<DirectChatMessageItem> Messages { get; } = [];

    /// <summary>Gets files that will be included with the next prompt.</summary>
    public ObservableCollection<DirectChatAttachmentItem> PendingAttachments { get; } = [];

    /// <summary>Gets agent tool-group choices for the selected conversation.</summary>
    public ObservableCollection<ToolGroupSelectionItem> ToolSelections { get; } = new(
        ToolGroups.TaskWhitelist.Select(static group => new ToolGroupSelectionItem(group, false)));

    /// <summary>Gets sensitive capability choices for the selected conversation.</summary>
    public ObservableCollection<TaskCapabilitySelectionItem> CapabilitySelections { get; } = new(
        TaskCapabilityCatalog.Definitions.Select(static definition => new TaskCapabilitySelectionItem(definition)));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteConversationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(AttachFileCommand))]
    private DirectChatConversationItem? _selectedConversation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAgentProfileCommand))]
    private bool _agentEnabled;

    [ObservableProperty]
    private string _agentSummary = "Chat mode · no tools";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(AttachFileCommand))]
    private string _draftMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewConversationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteConversationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(AttachFileCommand))]
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

    partial void OnAgentEnabledChanged(bool value) => RefreshAgentSummary();

    /// <inheritdoc />
    public void Activate()
    {
        ProviderBadge = BuildProviderBadge();
        if (!_loaded && !LoadCommand.IsRunning)
        {
            LoadCommand.Execute(null);
        }
    }

    /// <summary>Refreshes composer commands after the WPF window regains keyboard focus.</summary>
    public void RefreshComposerCommands()
    {
        SendCommand.NotifyCanExecuteChanged();
        AttachFileCommand.NotifyCanExecuteChanged();
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

    [RelayCommand(CanExecute = nameof(CanAttach))]
    private async Task AttachFileAsync()
    {
        var path = _desktopInteraction.SelectOpenFile(
            "Attach file",
            "Supported files|*.txt;*.md;*.csv;*.json;*.xml;*.log;*.pdf;*.docx;*.xls;*.xlsx;*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp|Documents|*.pdf;*.docx;*.xls;*.xlsx|Text files|*.txt;*.md;*.csv;*.json;*.xml;*.log|Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var attachment = await DirectChatAttachmentLoader.LoadAsync(path).ConfigureAwait(true);
            PendingAttachments.Add(new DirectChatAttachmentItem(attachment));
            SendCommand.NotifyCanExecuteChanged();
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(exception, "Direct Chat attachment").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void RemoveAttachment(DirectChatAttachmentItem? attachment)
    {
        if (attachment is not null) PendingAttachments.Remove(attachment);
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveAgentProfile))]
    private async Task SaveAgentProfileAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        if (selected is null) return;
        await _store.UpdateConversationAgentProfileAsync(
                selected.Id,
                AgentEnabled,
                ToolSelections.Where(static item => item.IsSelected).Select(static item => item.Name).ToArray(),
                CapabilitySelections.Where(static item => item.IsSelected).Select(static item => item.Capability).ToArray(),
                cancellationToken)
            .ConfigureAwait(true);
        RefreshAgentSummary();
        StatusText = AgentEnabled ? "Agent permissions saved for this conversation." : "Chat mode restored; tools are disabled.";
    }

    [RelayCommand(CanExecute = nameof(CanConvertToTask))]
    private async Task ConvertToTaskAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        var prompt = Messages.LastOrDefault(static message => message.IsUser)?.Content?.Trim();
        if (selected is null || string.IsNullOrWhiteSpace(prompt)) return;
        var id = "chat-" + Guid.NewGuid().ToString("N")[..10];
        var task = new ScheduledTask
        {
            Id = id,
            Name = selected.Title == "New chat" ? "Direct Chat task" : selected.Title,
            Schedule = "0 7 * * *",
            Prompt = prompt,
            Tools = ToolSelections.Where(static item => item.IsSelected).Select(static item => item.Name).ToArray(),
            Capabilities = CapabilitySelections.Where(static item => item.IsSelected).Select(static item => item.Capability).ToArray(),
            Enabled = false,
        };
        await _taskStore.SaveAsync(task, cancellationToken).ConfigureAwait(true);
        _messenger.Send(new EditTaskMessage(task.Id));
    }

    [RelayCommand(
        IncludeCancelCommand = true,
        CanExecute = nameof(CanSend),
        AllowConcurrentExecutions = true)]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        var text = DraftMessage.Trim();
        if (selected is null || (string.IsNullOrWhiteSpace(text) && PendingAttachments.Count == 0))
        {
            return;
        }

        IsStreaming = true;
        DirectChatMessage? assistantMessage = null;
        DirectChatMessageItem? assistantItem = null;
        try
        {
            var attachments = PendingAttachments.Select(static item => item.Attachment).ToArray();
            DraftMessage = string.Empty;
            PendingAttachments.Clear();
            var userMessage = await _store.AppendMessageAsync(
                    selected.Id,
                    DirectChatMessageRole.User,
                    text,
                    DirectChatMessageState.Completed,
                    _tokenEstimator.EstimateText(text),
                    cancellationToken,
                    attachments)
                .ConfigureAwait(true);
            Messages.Add(CreateMessageItem(userMessage));
            ConvertToTaskCommand.NotifyCanExecuteChanged();

            if (string.Equals(selected.Title, "New chat", StringComparison.Ordinal))
            {
                var titleSource = string.IsNullOrWhiteSpace(text)
                    ? $"Attachment: {attachments[0].FileName}"
                    : text;
                await _store.UpdateConversationTitleAsync(
                        selected.Id,
                        CreateTitle(titleSource),
                        cancellationToken)
                    .ConfigureAwait(true);
                selected.Title = CreateTitle(titleSource);
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
            var chunks = AgentEnabled
                ? _agentService.RunPromptStreamingAsync(
                    BuildAgentPrompt(plan.Messages),
                    ToolSelections.Where(static item => item.IsSelected).Select(static item => item.Name),
                    new ToolExecutionContext("direct-chat-" + selected.Id, [], CapabilitySelections.Where(static item => item.IsSelected).Select(static item => item.Capability)),
                    policy,
                    cancellationToken)
                : GetChatTextChunksAsync(client, plan.Messages, cancellationToken);
            await foreach (var textUpdate in chunks.ConfigureAwait(true))
            {
                if (string.IsNullOrEmpty(textUpdate))
                {
                    continue;
                }

                response.Append(textUpdate);
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
        SendCommand.NotifyCanExecuteChanged();
        if (value is null || _selectingConversation || IsStreaming)
        {
            return;
        }

        _ = LoadSelectedConversationSafelyAsync();
    }

    private bool CanSaveAgentProfile() => SelectedConversation is not null && !IsBusy && !IsStreaming;
    private bool CanConvertToTask() => SelectedConversation is not null && !IsBusy && !IsStreaming && Messages.Any(static message => message.IsUser);

    private void LoadAgentProfile(DirectChatConversation conversation)
    {
        AgentEnabled = conversation.AgentEnabled;
        var tools = conversation.Tools.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ToolSelections) item.IsSelected = tools.Contains(item.Name);
        var capabilities = conversation.Capabilities.ToHashSet();
        foreach (var item in CapabilitySelections) item.IsSelected = capabilities.Contains(item.Capability);
        RefreshAgentSummary();
    }

    private void RefreshAgentSummary()
    {
        var groups = ToolSelections.Where(static item => item.IsSelected).Select(static item => item.DisplayName).ToArray();
        AgentSummary = !AgentEnabled ? "Chat mode · no tools" : groups.Length == 0 ? "Agent mode · no tools selected" : "Agent mode · " + string.Join(", ", groups);
    }

    private static string BuildAgentPrompt(IReadOnlyList<ChatMessage> messages) => string.Join(
        "\n\n",
        messages.Where(static message => message.Role != ChatRole.System)
            .Select(static message => $"{message.Role}: {message.Text}"));

    private static async IAsyncEnumerable<string> GetChatTextChunksAsync(
        IChatClient client,
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in client.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            yield return update.Text;
        }
    }

    partial void OnDraftMessageChanged(string value) =>
        SendCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) =>
        SendCommand.NotifyCanExecuteChanged();

    partial void OnIsStreamingChanged(bool value) =>
        SendCommand.NotifyCanExecuteChanged();

    private bool CanMutateConversation() => !IsBusy && !IsStreaming;

    private bool CanDeleteConversation() =>
        CanMutateConversation() && SelectedConversation is not null;

    private bool CanSend() =>
        !IsBusy &&
        !IsStreaming &&
        SelectedConversation is not null &&
        (!string.IsNullOrWhiteSpace(DraftMessage) || PendingAttachments.Count > 0);

    private bool CanAttach() => !IsBusy && !IsStreaming && SelectedConversation is not null;

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
        LoadAgentProfile(conversation);
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

        var conversation = await RequireConversationAsync(selected.Id, cancellationToken).ConfigureAwait(true);
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
        LoadAgentProfile(conversation);
        ConvertToTaskCommand.NotifyCanExecuteChanged();
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
