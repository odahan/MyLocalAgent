using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    private const string EmptyResponseMessage =
        "The AI provider completed the request without returning response text. Try again or select a different model.";
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
    private readonly IDirectChatMediaFileService _mediaFileService;
    private readonly bool _isMarkdownAvailable;
    private bool _loaded;
    private bool _selectingConversation;
    private DirectChatConversationItem? _lastSelectedConversation;

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
        IWebView2AvailabilityService webViewAvailability,
        IDirectChatMediaFileService mediaFileService)
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
        _mediaFileService = mediaFileService;
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

    /// <summary>Gets local directories explicitly authorized for the selected conversation.</summary>
    public ObservableCollection<FileSystemAccessItem> AuthorizedLocations { get; } = [];

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateImageCommand))]
    private bool _supportsImageOutput;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateAudioCommand))]
    private bool _supportsAudioOutput;

    [ObservableProperty]
    private bool _supportsImageInput;

    partial void OnAgentEnabledChanged(bool value) => RefreshAgentSummary();

    /// <inheritdoc />
    public void Activate()
    {
        RefreshProviderCapabilities();
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
            RefreshProviderCapabilities();
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
            SupportsImageInput
                ? "Supported files|*.txt;*.md;*.csv;*.json;*.xml;*.log;*.pdf;*.docx;*.xls;*.xlsx;*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp|Documents|*.pdf;*.docx;*.xls;*.xlsx|Text files|*.txt;*.md;*.csv;*.json;*.xml;*.log|Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp"
                : "Documents|*.pdf;*.docx;*.xls;*.xlsx|Text files|*.txt;*.md;*.csv;*.json;*.xml;*.log");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var attachment = await DirectChatAttachmentLoader.LoadAsync(path).ConfigureAwait(true);
            if (attachment.Kind == DirectChatAttachmentKind.Image && !SupportsImageInput)
            {
                throw new NotSupportedException("The active provider has not declared image-input support.");
            }
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

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanGenerateImage))]
    private Task GenerateImageAsync(CancellationToken cancellationToken) =>
        GenerateMediaAsync(MultimodalGenerationKind.Image, cancellationToken);

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanGenerateAudio))]
    private Task GenerateAudioAsync(CancellationToken cancellationToken) =>
        GenerateMediaAsync(MultimodalGenerationKind.Audio, cancellationToken);

    [RelayCommand]
    private async Task SaveMediaAsync(DirectChatContentItem? item)
    {
        if (item?.Content.Data is null)
        {
            return;
        }

        var extension = item.Content.Kind == DirectChatContentKind.Image ? ".png" : ".wav";
        var filter = item.Content.Kind == DirectChatContentKind.Image
            ? "PNG image|*.png|All files|*.*"
            : "WAV audio|*.wav|All files|*.*";
        var path = _desktopInteraction.SelectSavePath(
            "Save generated media",
            filter,
            extension,
            item.Content.FileName ?? $"direct-chat-media{extension}");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await _mediaFileService.SaveAsync(item.Content, path, CancellationToken.None)
            .ConfigureAwait(true);
        StatusText = "Media saved locally.";
    }

    [RelayCommand]
    private void OpenImage(DirectChatContentItem? item)
    {
        if (item?.Content is { Kind: DirectChatContentKind.Image, Data: not null } content)
        {
            _desktopInteraction.ShowImage(content.Data, content.FileName ?? "Generated image");
        }
    }

    [RelayCommand]
    private void PlayAudio(DirectChatContentItem? item)
    {
        if (item?.Content is { Kind: DirectChatContentKind.Audio, Data: not null } content)
        {
            _desktopInteraction.PlayWaveAudio(content.Data);
        }
    }

    [RelayCommand]
    private void StopAudio() => _desktopInteraction.StopAudio();

    [RelayCommand]
    private void CopyMessage(DirectChatMessageItem? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Content))
        {
            return;
        }

        _desktopInteraction.CopyText(message.Content);
        StatusText = "Message copied to the clipboard.";
    }

    [RelayCommand]
    private void CancelGeneration()
    {
        if (SendCancelCommand.CanExecute(null))
        {
            SendCancelCommand.Execute(null);
        }

        if (GenerateImageCancelCommand.CanExecute(null))
        {
            GenerateImageCancelCommand.Execute(null);
        }

        if (GenerateAudioCancelCommand.CanExecute(null))
        {
            GenerateAudioCancelCommand.Execute(null);
        }
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
                AuthorizedLocations.Select(static item => item.ToRule()).ToArray(),
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
            FileSystemAccess = AuthorizedLocations.Select(static item => item.ToRule()).ToArray(),
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
                    new ToolExecutionContext(
                        "direct-chat-" + selected.Id,
                        AuthorizedLocations.Select(static item => item.ToRule()),
                        CapabilitySelections.Where(static item => item.IsSelected).Select(static item => item.Capability)),
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

            if (string.IsNullOrWhiteSpace(response.ToString()))
            {
                assistantItem.Update(EmptyResponseMessage, DirectChatMessageState.Failed);
                throw new InvalidOperationException(EmptyResponseMessage);
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
        RefreshGenerationCommands();
        if (value is null)
        {
            if (!_selectingConversation &&
                _lastSelectedConversation is { } previous &&
                Conversations.Contains(previous))
            {
                SelectConversationWithoutAutomaticLoad(previous);
            }

            return;
        }

        _lastSelectedConversation = value;
        if (_selectingConversation || IsStreaming)
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
        AuthorizedLocations.Clear();
        foreach (var rule in conversation.FileSystemAccess)
        {
            AuthorizedLocations.Add(new FileSystemAccessItem(rule.Path, rule.Access));
        }
        RefreshAgentSummary();
    }

    [RelayCommand]
    private void AddAuthorizedLocation()
    {
        var selectedPath = _desktopInteraction.SelectFolder(
            "Select an authorized Direct Chat location");
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedPath));
        if (AuthorizedLocations.Any(item => string.Equals(
                item.Path,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"Location '{normalizedPath}' is already authorized.";
            return;
        }

        var root = Path.GetPathRoot(normalizedPath);
        if (string.Equals(normalizedPath, Path.TrimEndingDirectorySeparator(root ?? string.Empty), StringComparison.OrdinalIgnoreCase) &&
            !_desktopInteraction.Confirm(
                "Authorize an entire volume?",
                $"This grants this conversation access to the entire volume '{normalizedPath}' and all descendants. Continue?"))
        {
            return;
        }

        AuthorizedLocations.Add(new FileSystemAccessItem(normalizedPath, FileSystemAccessMode.ReadWrite));
        StatusText = $"Location '{normalizedPath}' added with read and write access. Save permissions to apply it.";
    }

    [RelayCommand]
    private void RemoveAuthorizedLocation(FileSystemAccessItem? item)
    {
        if (item is not null)
        {
            AuthorizedLocations.Remove(item);
        }
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

    partial void OnDraftMessageChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
        RefreshGenerationCommands();
    }

    partial void OnIsBusyChanged(bool value)
    {
        SendCommand.NotifyCanExecuteChanged();
        RefreshGenerationCommands();
    }

    partial void OnIsStreamingChanged(bool value) =>
        RefreshGenerationCommands();

    private bool CanMutateConversation() => !IsBusy && !IsStreaming;

    private bool CanDeleteConversation() =>
        CanMutateConversation() && SelectedConversation is not null;

    private bool CanSend() =>
        !IsBusy &&
        !IsStreaming &&
        SelectedConversation is not null &&
        (!string.IsNullOrWhiteSpace(DraftMessage) || PendingAttachments.Count > 0);

    private bool CanAttach() => !IsBusy && !IsStreaming && SelectedConversation is not null;

    private bool CanGenerateImage() =>
        SupportsImageOutput && CanGenerateMedia();

    private bool CanGenerateAudio() =>
        SupportsAudioOutput && CanGenerateMedia();

    private bool CanGenerateMedia() =>
        !IsBusy && !IsStreaming && SelectedConversation is not null && !string.IsNullOrWhiteSpace(DraftMessage);

    private bool CanLoadOlderMessages() =>
        HasOlderMessages && !IsBusy && !IsStreaming;

    private async Task GenerateMediaAsync(
        MultimodalGenerationKind kind,
        CancellationToken cancellationToken)
    {
        var selected = SelectedConversation;
        var prompt = DraftMessage.Trim();
        if (selected is null || string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        IsStreaming = true;
        DirectChatMessage? assistantMessage = null;
        DirectChatMessageItem? assistantItem = null;
        try
        {
            var provider = _llmClientFactory.Create();
            var required = kind == MultimodalGenerationKind.Image
                ? ProviderCapabilities.ImageOutput
                : ProviderCapabilities.AudioOutput;
            if (!provider.Capabilities.HasFlag(required))
            {
                throw new NotSupportedException(
                    $"The active provider does not declare {kind.ToString().ToLowerInvariant()} output support.");
            }

            DraftMessage = string.Empty;
            var userMessage = await _store.AppendMessageAsync(
                    selected.Id,
                    DirectChatMessageRole.User,
                    prompt,
                    DirectChatMessageState.Completed,
                    _tokenEstimator.EstimateText(prompt),
                    cancellationToken)
                .ConfigureAwait(true);
            Messages.Add(CreateMessageItem(userMessage));
            if (string.Equals(selected.Title, "New chat", StringComparison.Ordinal))
            {
                var title = CreateTitle(prompt);
                await _store.UpdateConversationTitleAsync(selected.Id, title, cancellationToken)
                    .ConfigureAwait(true);
                selected.Title = title;
            }

            assistantMessage = await _store.AppendMessageAsync(
                    selected.Id,
                    DirectChatMessageRole.Assistant,
                    string.Empty,
                    DirectChatMessageState.Pending,
                    null,
                    cancellationToken)
                .ConfigureAwait(true);
            assistantItem = CreateMessageItem(assistantMessage);
            Messages.Add(assistantItem);
            StatusText = $"Waiting to generate {kind.ToString().ToLowerInvariant()}…";
            assistantItem.Update(string.Empty, DirectChatMessageState.Streaming);
            await _store.UpdateMessageAsync(
                    assistantMessage.Id,
                    string.Empty,
                    DirectChatMessageState.Streaming,
                    null,
                    cancellationToken)
                .ConfigureAwait(true);
            StatusText = $"Generating {kind.ToString().ToLowerInvariant()}…";

            var generated = await provider.GenerateMediaAsync(
                    new MultimodalGenerationRequest { Kind = kind, Prompt = prompt },
                    cancellationToken)
                .ConfigureAwait(true);
            var content = new DirectChatContent
            {
                Position = 0,
                Kind = generated.Kind == MultimodalGenerationKind.Image
                    ? DirectChatContentKind.Image
                    : DirectChatContentKind.Audio,
                MediaType = generated.MediaType,
                FileName = generated.FileName,
                Data = generated.Data,
                SizeBytes = generated.Data.LongLength,
                Duration = generated.Duration,
                IsCloudContent = !string.Equals(
                    provider.ProviderName,
                    AIOptions.OllamaProvider,
                    StringComparison.OrdinalIgnoreCase),
                ProviderName = provider.ProviderName,
            };
            assistantItem.UpdateContents(
                string.Empty,
                [content],
                DirectChatMessageState.Completed);
            await _store.UpdateMessageAsync(
                    assistantMessage.Id,
                    string.Empty,
                    DirectChatMessageState.Completed,
                    null,
                    cancellationToken,
                    [content])
                .ConfigureAwait(true);
            await RefreshSelectedConversationMetadataAsync(cancellationToken).ConfigureAwait(true);
            StatusText = $"{kind} generation completed.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (assistantMessage is not null && assistantItem is not null)
            {
                assistantItem.UpdateContents(
                    assistantItem.Content,
                    assistantItem.Contents.Select(static item => item.Content).ToArray(),
                    DirectChatMessageState.Canceled);
                await TryFinalizeMessageAsync(
                        assistantMessage.Id,
                        assistantItem.Content,
                        DirectChatMessageState.Canceled)
                    .ConfigureAwait(true);
            }

            StatusText = $"{kind} generation canceled.";
        }
        catch (Exception exception)
        {
            if (assistantMessage is not null && assistantItem is not null)
            {
                assistantItem.UpdateContents(
                    assistantItem.Content,
                    assistantItem.Contents.Select(static item => item.Content).ToArray(),
                    DirectChatMessageState.Failed);
                await TryFinalizeMessageAsync(
                        assistantMessage.Id,
                        assistantItem.Content,
                        DirectChatMessageState.Failed)
                    .ConfigureAwait(true);
            }

            await HandleExceptionAsync(exception, $"Direct Chat {kind} generation")
                .ConfigureAwait(true);
        }
        finally
        {
            IsStreaming = false;
        }
    }

    private void RefreshProviderCapabilities()
    {
        try
        {
            var provider = _llmClientFactory.Create();
            ProviderBadge = $"{provider.ProviderName} · {provider.ModelName}";
            SupportsImageInput = provider.Capabilities.HasFlag(ProviderCapabilities.ImageInput);
            SupportsImageOutput = provider.Capabilities.HasFlag(ProviderCapabilities.ImageOutput);
            SupportsAudioOutput = provider.Capabilities.HasFlag(ProviderCapabilities.AudioOutput);
        }
        catch
        {
            ProviderBadge = "Provider not configured";
            SupportsImageInput = false;
            SupportsImageOutput = false;
            SupportsAudioOutput = false;
        }

        RefreshGenerationCommands();
    }

    private void RefreshGenerationCommands()
    {
        SendCommand.NotifyCanExecuteChanged();
        GenerateImageCommand.NotifyCanExecuteChanged();
        GenerateAudioCommand.NotifyCanExecuteChanged();
    }

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
        var currentIndex = Conversations.IndexOf(selected);
        if (currentIndex <= 0)
        {
            return;
        }

        _selectingConversation = true;
        try
        {
            Conversations.Move(currentIndex, 0);
            SelectedConversation = selected;
        }
        finally
        {
            _selectingConversation = false;
        }
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
