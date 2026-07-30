using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Coordinates memory-only AI workflow authoring and explanation.
/// </summary>
public sealed partial class AIWorkflowHelperViewModel : ObservableObject
{
    private const int MaximumAttempts = 3;
    private const string WebView2DownloadUri =
        "https://developer.microsoft.com/microsoft-edge/webview2/";

    private readonly IWorkflowAssistantService _assistant;
    private readonly IMarkdownRenderingService _markdown;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private AIWorkflowHelperRequest? _request;
    private IReadOnlyList<WorkflowStepDefinition>? _candidate;
    private WorkflowRequirementAnalysis? _analysis;
    private CancellationTokenSource? _activeCancellation;
    private OperationKind _lastOperation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefineCommand))]
    private AIWorkflowHelperMode _mode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefineCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private AIWorkflowHelperState _state = AIWorkflowHelperState.Idle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefineCommand))]
    private string _refinement = string.Empty;

    [ObservableProperty]
    private string _responseText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Describe the workflow you want to create.";

    [ObservableProperty]
    private string _plainTextExplanation = string.Empty;

    [ObservableProperty]
    private string _renderedHtml = string.Empty;

    [ObservableProperty]
    private bool _showRenderedMarkdown;

    [ObservableProperty]
    private bool _isRenderedAvailable;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIWorkflowHelperViewModel"/> class.
    /// </summary>
    /// <param name="assistant">The tool-less streaming assistant.</param>
    /// <param name="markdown">The safe Markdown renderer.</param>
    /// <param name="incidentReporter">The application incident pipeline.</param>
    /// <param name="desktopInteraction">Explicit desktop interactions.</param>
    /// <param name="webViewAvailability">The optional runtime detector.</param>
    public AIWorkflowHelperViewModel(
        IWorkflowAssistantService assistant,
        IMarkdownRenderingService markdown,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IWebView2AvailabilityService? webViewAvailability = null)
    {
        ArgumentNullException.ThrowIfNull(assistant);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(incidentReporter);
        ArgumentNullException.ThrowIfNull(desktopInteraction);
        _assistant = assistant;
        _markdown = markdown;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        IsRenderedAvailable =
            (webViewAvailability ?? new WebView2AvailabilityService()).IsAvailable();
    }

    /// <summary>
    /// Gets deterministic validation errors for the latest attempt.
    /// </summary>
    public ObservableCollection<string> ValidationErrors { get; } = [];

    /// <summary>
    /// Gets explicit tool-group and filesystem proposals.
    /// </summary>
    public ObservableCollection<WorkflowProposalItem> Proposals { get; } = [];

    /// <summary>
    /// Gets deterministic risk and authorization warnings.
    /// </summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>
    /// Gets the immutable applied result, when the user applied authoring changes.
    /// </summary>
    public AIWorkflowHelperResult? Result { get; private set; }

    /// <summary>
    /// Gets a value indicating whether authoring controls are visible.
    /// </summary>
    public bool IsAuthorMode => Mode == AIWorkflowHelperMode.Author;

    /// <summary>
    /// Gets a value indicating whether explanation controls are visible.
    /// </summary>
    public bool IsExplainMode => Mode == AIWorkflowHelperMode.Explain;

    /// <summary>
    /// Gets a value indicating whether a provider request is active.
    /// </summary>
    public bool IsBusy => State is AIWorkflowHelperState.Generating or AIWorkflowHelperState.Validating;

    /// <summary>
    /// Gets a value indicating whether required proposal review is visible.
    /// </summary>
    public bool HasReview => Proposals.Count > 0 || Warnings.Count > 0;

    /// <summary>
    /// Gets a value indicating whether rendered Markdown is unavailable.
    /// </summary>
    public bool IsRenderedUnavailable => !IsRenderedAvailable;

    /// <summary>
    /// Gets the helper window title.
    /// </summary>
    public string Title => IsExplainMode ? "AI Workflow Helper · Explain" : "AI Workflow Helper · Author";

    /// <summary>
    /// Raised when a command requests the modal window to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Initializes the memory-only session from a task draft.
    /// </summary>
    /// <param name="request">The validated task draft context.</param>
    public void Initialize(AIWorkflowHelperRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        Mode = request.Mode;
        OnPropertyChanged(nameof(IsAuthorMode));
        OnPropertyChanged(nameof(IsExplainMode));
        OnPropertyChanged(nameof(Title));
        Result = null;
        ValidationErrors.Clear();
        Warnings.Clear();
        ClearProposals();

        if (request.Workflow.Count > 0)
        {
            Description = WorkflowDescriptionFormatter.Format(request.Workflow);
            if (IsAuthorMode)
            {
                try
                {
                    ScheduledTaskValidator.ValidateWorkflowDefinitions(
                        request.Workflow,
                        request.ToolGroups);
                    AcceptCandidate(request.Workflow, updateResponse: false);
                }
                catch (WorkflowValidationException)
                {
                    _candidate = null;
                    State = AIWorkflowHelperState.Idle;
                    StatusText =
                        "The parsed workflow has validation errors. Edit its complete description and Generate a replacement.";
                }
            }
            else
            {
                _candidate = request.Workflow.ToArray();
            }
        }
        else
        {
            _candidate = null;
            Description = string.Empty;
            State = AIWorkflowHelperState.Idle;
        }

        if (IsExplainMode)
        {
            StatusText = "Ready to explain the locally validated workflow.";
        }
    }

    /// <summary>
    /// Clears assistant-session content and cancels any active request.
    /// </summary>
    public void Shutdown()
    {
        _activeCancellation?.Cancel();
        _activeCancellation?.Dispose();
        _activeCancellation = null;
        Description = string.Empty;
        Refinement = string.Empty;
        ResponseText = string.Empty;
        PlainTextExplanation = string.Empty;
        RenderedHtml = string.Empty;
        ValidationErrors.Clear();
        Warnings.Clear();
        ClearProposals();
        _candidate = null;
        _analysis = null;
        _request = null;
        Result = null;
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task GenerateAsync() =>
        Mode == AIWorkflowHelperMode.Explain
            ? RunExplainAsync()
            : RunAuthorAsync(OperationKind.Generate);

    [RelayCommand(CanExecute = nameof(CanRefine))]
    private Task RefineAsync() =>
        RunAuthorAsync(OperationKind.Refine);

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() =>
        _lastOperation == OperationKind.Explain
            ? RunExplainAsync()
            : RunAuthorAsync(
                _lastOperation == OperationKind.Refine
                    ? OperationKind.Refine
                    : OperationKind.Generate);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() =>
        _activeCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (_request is null || _candidate is null || _analysis is null)
        {
            return;
        }

        var tools = _request.ToolGroups
            .Concat(_analysis.RequiredToolGroups)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rules = MergeRules(
            _request.FileSystemAccess,
            _analysis.MissingPathRequirements.Select(
                static requirement => new FileSystemAccessRule
                {
                    Path = requirement.DirectoryPath,
                    Access = requirement.RequiredAccess,
                }));
        Result = new AIWorkflowHelperResult(
            WorkflowDefinitionSerializer.Serialize(_candidate),
            tools,
            rules);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Copy()
    {
        if (!string.IsNullOrEmpty(ResponseText))
        {
            _desktopInteraction.CopyText(ResponseText);
            StatusText = "Explanation copied to the clipboard.";
        }
    }

    [RelayCommand]
    private void OpenWebView2DownloadPage() =>
        _desktopInteraction.OpenUri(WebView2DownloadUri);

    [RelayCommand]
    private void Close()
    {
        _activeCancellation?.Cancel();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanGenerate() =>
        !IsBusy &&
        (Mode == AIWorkflowHelperMode.Explain || !string.IsNullOrWhiteSpace(Description));

    private bool CanRefine() =>
        Mode == AIWorkflowHelperMode.Author &&
        !IsBusy &&
        _candidate is not null &&
        !string.IsNullOrWhiteSpace(Refinement);

    private bool CanRetry() =>
        !IsBusy &&
        State is AIWorkflowHelperState.Failed or AIWorkflowHelperState.Cancelled;

    private bool CanCancel() => IsBusy;

    private bool CanApply() =>
        Mode == AIWorkflowHelperMode.Author &&
        !IsBusy &&
        _candidate is not null &&
        State == AIWorkflowHelperState.Ready &&
        Proposals.All(static proposal => proposal.IsAccepted);

    private async Task RunAuthorAsync(OperationKind operation)
    {
        if (_request is null)
        {
            return;
        }

        _lastOperation = operation;
        ValidationErrors.Clear();
        var baseMessage = operation == OperationKind.Refine
            ? BuildRefinementMessage()
            : BuildGenerationMessage();
        var errors = Array.Empty<string>();
        string previousResponse = string.Empty;
        Exception? providerFailure = null;

        using var cancellation = new CancellationTokenSource();
        _activeCancellation = cancellation;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            State = AIWorkflowHelperState.Generating;
            StatusText = $"Streaming attempt {attempt} of {MaximumAttempts}…";
            ResponseText = string.Empty;
            var userMessage = attempt == 1
                ? baseMessage
                : BuildCorrectionMessage(baseMessage, previousResponse, errors);
            try
            {
                var response = new StringBuilder();
                await foreach (var chunk in _assistant.StreamGenerationAsync(
                    userMessage,
                    cancellation.Token))
                {
                    response.Append(chunk);
                    ResponseText = response.ToString();
                }

                previousResponse = response.ToString();
                State = AIWorkflowHelperState.Validating;
                if (TryAcceptAuthorResponse(previousResponse, out errors))
                {
                    if (operation == OperationKind.Refine)
                    {
                        Refinement = string.Empty;
                    }

                    StatusText = Proposals.Count == 0
                        ? "The workflow is valid and ready to apply. No operation was executed."
                        : "The workflow is valid. Accept every required proposal before applying. No operation was executed.";
                    _activeCancellation = null;
                    return;
                }

                providerFailure = null;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                State = AIWorkflowHelperState.Cancelled;
                StatusText = "Generation cancelled. The visible response is incomplete and cannot be applied.";
                _activeCancellation = null;
                return;
            }
            catch (Exception exception)
            {
                providerFailure = exception;
                errors = ["The configured provider did not complete this attempt."];
            }
        }

        State = AIWorkflowHelperState.Failed;
        ValidationErrors.Clear();
        foreach (var error in errors)
        {
            ValidationErrors.Add(error);
        }

        if (providerFailure is not null)
        {
            var correlationId = await ReportProviderFailureAsync().ConfigureAwait(true);
            StatusText = $"The configured AI provider could not complete the request. Correlation id: {correlationId}";
        }
        else
        {
            StatusText = "Three attempts were unsuccessful. The last response and deterministic errors remain visible; use Retry.";
        }

        _activeCancellation = null;
    }

    private async Task RunExplainAsync()
    {
        if (_request is null || _candidate is null)
        {
            return;
        }

        _lastOperation = OperationKind.Explain;
        ValidationErrors.Clear();
        var baseMessage = BuildExplanationMessage();
        var errors = Array.Empty<string>();
        string previousResponse = string.Empty;
        Exception? providerFailure = null;
        using var cancellation = new CancellationTokenSource();
        _activeCancellation = cancellation;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            State = AIWorkflowHelperState.Generating;
            StatusText = $"Streaming explanation attempt {attempt} of {MaximumAttempts}…";
            ResponseText = string.Empty;
            var userMessage = attempt == 1
                ? baseMessage
                : BuildCorrectionMessage(baseMessage, previousResponse, errors);
            try
            {
                var response = new StringBuilder();
                await foreach (var chunk in _assistant.StreamExplanationAsync(
                    userMessage,
                    cancellation.Token))
                {
                    response.Append(chunk);
                    ResponseText = response.ToString();
                    PlainTextExplanation = ResponseText;
                }

                previousResponse = response.ToString();
                if (string.IsNullOrWhiteSpace(previousResponse))
                {
                    errors = ["The explanation response was empty."];
                    providerFailure = null;
                    continue;
                }

                PlainTextExplanation = _markdown.ToPlainText(previousResponse);
                RenderedHtml = _markdown.ToRestrictedHtml(previousResponse);
                State = AIWorkflowHelperState.Ready;
                StatusText = "Explanation complete. Text and Rendered Markdown use the same response.";
                _activeCancellation = null;
                return;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                State = AIWorkflowHelperState.Cancelled;
                StatusText = "Explanation cancelled. The visible response is incomplete.";
                _activeCancellation = null;
                return;
            }
            catch (Exception exception)
            {
                providerFailure = exception;
                errors = ["The configured provider did not complete this attempt."];
            }
        }

        State = AIWorkflowHelperState.Failed;
        foreach (var error in errors)
        {
            ValidationErrors.Add(error);
        }

        if (providerFailure is not null)
        {
            var correlationId = await ReportProviderFailureAsync().ConfigureAwait(true);
            StatusText = $"The configured AI provider could not complete the explanation. Correlation id: {correlationId}";
        }
        else
        {
            StatusText = "Three explanation attempts were unsuccessful. Use Retry.";
        }

        _activeCancellation = null;
    }

    private bool TryAcceptAuthorResponse(
        string response,
        out string[] errors)
    {
        try
        {
            var workflow = WorkflowDefinitionSerializer.Deserialize(response);
            var analysis = WorkflowRequirementAnalyzer.Analyze(
                workflow,
                _request!.ToolGroups,
                _request.FileSystemAccess,
                _request.Safety);
            var provisionalGroups = _request.ToolGroups
                .Concat(analysis.RequiredToolGroups)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var provisionalRules = MergeRules(
                _request.FileSystemAccess,
                analysis.MissingPathRequirements.Select(
                    static requirement => new FileSystemAccessRule
                    {
                        Path = requirement.DirectoryPath,
                        Access = requirement.RequiredAccess,
                    }));
            ScheduledTaskValidator.Validate(
                new ScheduledTask
                {
                    Id = string.IsNullOrWhiteSpace(_request.TaskId) ? "workflow-preview" : _request.TaskId.Trim(),
                    Name = string.IsNullOrWhiteSpace(_request.TaskName) ? "Workflow preview" : _request.TaskName.Trim(),
                    Schedule = string.IsNullOrWhiteSpace(_request.Schedule) ? "0 7 * * *" : _request.Schedule.Trim(),
                    Prompt = string.IsNullOrWhiteSpace(_request.Prompt) ? "Workflow preview" : _request.Prompt,
                    Tools = provisionalGroups,
                    FileSystemAccess = provisionalRules,
                    Workflow = workflow,
                    Enabled = false,
                    ErrorPolicy = _request.ErrorPolicy,
                });
            AcceptCandidate(workflow, analysis);
            errors = [];
            return true;
        }
        catch (Exception exception) when (
            exception is WorkflowValidationException or
                TaskValidationException or
                System.Text.Json.JsonException or
                ArgumentException)
        {
            errors = [exception.Message];
            ValidationErrors.Clear();
            ValidationErrors.Add(exception.Message);
            State = AIWorkflowHelperState.Failed;
            return false;
        }
    }

    private void AcceptCandidate(
        IReadOnlyList<WorkflowStepDefinition> workflow,
        WorkflowRequirementAnalysis? knownAnalysis = null,
        bool updateResponse = true)
    {
        _candidate = workflow.ToArray();
        _analysis = knownAnalysis ?? WorkflowRequirementAnalyzer.Analyze(
            workflow,
            _request!.ToolGroups,
            _request.FileSystemAccess,
            _request.Safety);
        Description = WorkflowDescriptionFormatter.Format(workflow);
        if (updateResponse)
        {
            ValidationErrors.Clear();
        }

        BuildReview(_analysis);
    }

    private void BuildReview(WorkflowRequirementAnalysis analysis)
    {
        ClearProposals();
        Warnings.Clear();
        foreach (var group in analysis.MissingToolGroups)
        {
            AddProposal(
                new WorkflowProposalItem(
                    "Tool group",
                    group,
                    $"Enable task tool group '{group}'."));
        }

        foreach (var path in analysis.MissingPathRequirements)
        {
            var qualifiers = new List<string>();
            if (path.IsBroaderScope)
            {
                qualifiers.Add("broader containing-directory scope");
            }

            if (path.IsVolumeRoot)
            {
                qualifiers.Add("entire volume");
            }

            if (path.IsUnc)
            {
                qualifiers.Add("UNC path; S4U may not have reusable Windows credentials");
            }

            AddProposal(
                new WorkflowProposalItem(
                    "Filesystem",
                    path.DirectoryPath,
                    $"Authorize '{path.DirectoryPath}' as {path.RequiredAccess} for {string.Join(", ", path.SourceAliases)}" +
                    (qualifiers.Count == 0 ? "." : $" ({string.Join("; ", qualifiers)}).")));
        }

        foreach (var warning in analysis.Warnings)
        {
            Warnings.Add(warning);
        }

        foreach (var safety in analysis.SafetyRequirements)
        {
            Warnings.Add(
                $"{safety.ConfigurationKey}: {(safety.IsEnabled ? "enabled" : "disabled")} ({string.Join(", ", safety.SourceAliases)}).");
        }

        OnPropertyChanged(nameof(HasReview));
        State = Proposals.Count == 0
            ? AIWorkflowHelperState.Ready
            : AIWorkflowHelperState.NeedsApproval;
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private void AddProposal(WorkflowProposalItem proposal)
    {
        proposal.PropertyChanged += OnProposalChanged;
        Proposals.Add(proposal);
    }

    private void ClearProposals()
    {
        foreach (var proposal in Proposals)
        {
            proposal.PropertyChanged -= OnProposalChanged;
        }

        Proposals.Clear();
        OnPropertyChanged(nameof(HasReview));
    }

    private void OnProposalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkflowProposalItem.IsAccepted))
        {
            State = Proposals.All(static item => item.IsAccepted)
                ? AIWorkflowHelperState.Ready
                : AIWorkflowHelperState.NeedsApproval;
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    private string BuildGenerationMessage() =>
        $"""
        Create a complete workflow array from this editable English description:
        <<<WORKFLOW-DESCRIPTION
        {Description}
        END-WORKFLOW-DESCRIPTION>>>
        Current task tool groups: {string.Join(", ", _request!.ToolGroups)}
        Current task filesystem rules: {DescribeRules(_request.FileSystemAccess)}
        """;

    private string BuildRefinementMessage() =>
        $"""
        Replace the current workflow with a complete refined workflow array.
        Current canonical workflow:
        {WorkflowDefinitionSerializer.Serialize(_candidate!)}
        Current deterministic description:
        {Description}
        Latest refinement instruction:
        {Refinement}
        """;

    private string BuildExplanationMessage()
    {
        _analysis = WorkflowRequirementAnalyzer.Analyze(
            _candidate!,
            _request!.ToolGroups,
            _request.FileSystemAccess,
            _request.Safety);
        return
            $"""
            Canonical workflow:
            {WorkflowDefinitionSerializer.Serialize(_candidate!)}

            Deterministic application analysis:
            Required tool groups: {string.Join(", ", _analysis.RequiredToolGroups)}
            Missing tool groups: {string.Join(", ", _analysis.MissingToolGroups)}
            Missing filesystem grants: {string.Join("; ", _analysis.MissingPathRequirements.Select(static item => $"{item.DirectoryPath} ({item.RequiredAccess})"))}
            Safety gates: {string.Join("; ", _analysis.SafetyRequirements.Select(static item => $"{item.ConfigurationKey}={(item.IsEnabled ? "enabled" : "disabled")}"))}
            Warnings: {string.Join("; ", _analysis.Warnings)}
            """;
    }

    private static string BuildCorrectionMessage(
        string originalMessage,
        string previousResponse,
        IReadOnlyList<string> errors) =>
        $"""
        Return a complete replacement response, not a patch. Application validation cannot be waived or reinterpreted.
        Previous raw response:
        <<<PREVIOUS-RESPONSE
        {previousResponse}
        END-PREVIOUS-RESPONSE>>>
        Exact deterministic errors:
        {string.Join(Environment.NewLine, errors.Select(static error => "- " + error))}
        Original request:
        {originalMessage}
        """;

    private static string DescribeRules(
        IReadOnlyList<FileSystemAccessRule> rules) =>
        rules.Count == 0
            ? "<none>"
            : string.Join(
                "; ",
                rules.Select(static rule => $"{rule.Path} ({rule.Access})"));

    private static IReadOnlyList<FileSystemAccessRule> MergeRules(
        IEnumerable<FileSystemAccessRule> current,
        IEnumerable<FileSystemAccessRule> proposed) =>
        current
            .Concat(proposed)
            .GroupBy(
                static rule => ScheduledTaskValidator.NormalizeAccessPath(rule.Path),
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group => new FileSystemAccessRule
                {
                    Path = group.Key,
                    Access = group.Any(static rule => rule.Access == FileSystemAccessMode.ReadWrite)
                        ? FileSystemAccessMode.ReadWrite
                        : FileSystemAccessMode.ReadOnly,
                })
            .OrderBy(static rule => rule.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private async Task<string> ReportProviderFailureAsync() =>
        await _incidentReporter.ReportAsync(
            new InvalidOperationException(
                "The AI Workflow Helper provider operation failed."),
            "AI Workflow Helper",
            CancellationToken.None).ConfigureAwait(true);

    partial void OnStateChanged(AIWorkflowHelperState value)
    {
        OnPropertyChanged(nameof(IsBusy));
        GenerateCommand.NotifyCanExecuteChanged();
        RefineCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnModeChanged(AIWorkflowHelperMode value)
    {
        OnPropertyChanged(nameof(IsAuthorMode));
        OnPropertyChanged(nameof(IsExplainMode));
        OnPropertyChanged(nameof(Title));
    }

    partial void OnIsRenderedAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRenderedUnavailable));
        if (!value)
        {
            ShowRenderedMarkdown = false;
        }
    }

    private enum OperationKind
    {
        Generate,
        Refine,
        Explain,
    }
}
