using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.Services;
using Serilog.Events;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Manages provider, general, log, and incident settings.
/// </summary>
public sealed partial class SettingsViewModel :
    ObservableRecipient,
    IRecipient<IncidentNotificationMessage>,
    IActivatablePage
{
    private static readonly HashSet<string> EditablePropertyNames =
    [
        nameof(IsOllamaProvider),
        nameof(IsOpenAIProvider),
        nameof(OllamaEndpoint),
        nameof(OllamaModel),
        nameof(OllamaTimeoutSeconds),
        nameof(OpenAIEndpoint),
        nameof(OpenAIModel),
        nameof(OpenAITimeoutSeconds),
        nameof(ApiKeyReplacement),
        nameof(FileSystemRootPath),
        nameof(HttpTimeoutSeconds),
        nameof(HttpUserAgent),
        nameof(AllowedHostsText),
        nameof(RunWhenLoggedOff),
        nameof(RunnerPath),
        nameof(AllowSendEmail),
        nameof(AllowFileDelete),
        nameof(AllowHttpPost),
        nameof(LoggingMinimumLevel),
        nameof(SelectedTheme),
    ];

    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<OpenAIOptions> _openAIOptions;
    private readonly IOptionsMonitor<FileSystemOptions> _fileSystemOptions;
    private readonly IOptionsMonitor<HttpOptions> _httpOptions;
    private readonly IOptionsMonitor<SchedulerOptions> _schedulerOptions;
    private readonly IOptionsMonitor<SafetyOptions> _safetyOptions;
    private readonly IOptionsMonitor<LoggingOptions> _loggingOptions;
    private readonly ILlmClientFactory _llmClientFactory;
    private readonly ISecretStore _secretStore;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IThemeService _themeService;
    private readonly IIncidentLogService _incidentLogService;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IInMemoryLogSink _logSink;
    private readonly ILogManagementService _logManagementService;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _timeProvider;
    private bool _isLoading;
    private bool _hasLoaded;

    [ObservableProperty]
    private bool _isOllamaProvider = true;

    [ObservableProperty]
    private bool _isOpenAIProvider;

    [ObservableProperty]
    private string _ollamaEndpoint = string.Empty;

    [ObservableProperty]
    private string _ollamaModel = string.Empty;

    [ObservableProperty]
    private int _ollamaTimeoutSeconds;

    [ObservableProperty]
    private string _openAIEndpoint = string.Empty;

    [ObservableProperty]
    private string _openAIModel = string.Empty;

    [ObservableProperty]
    private int _openAITimeoutSeconds;

    [ObservableProperty]
    private string _apiKeyReplacement = string.Empty;

    [ObservableProperty]
    private bool _isApiKeyConfigured;

    [ObservableProperty]
    private string _fileSystemRootPath = string.Empty;

    [ObservableProperty]
    private int _httpTimeoutSeconds;

    [ObservableProperty]
    private string _httpUserAgent = string.Empty;

    [ObservableProperty]
    private string _allowedHostsText = string.Empty;

    [ObservableProperty]
    private bool _runWhenLoggedOff;

    [ObservableProperty]
    private string _runnerPath = string.Empty;

    [ObservableProperty]
    private string _resolvedRunnerPath = string.Empty;

    [ObservableProperty]
    private bool _allowSendEmail;

    [ObservableProperty]
    private bool _allowFileDelete;

    [ObservableProperty]
    private bool _allowHttpPost;

    [ObservableProperty]
    private string _loggingMinimumLevel = nameof(LogEventLevel.Information);

    [ObservableProperty]
    private string _selectedTheme = AppThemeCatalog.DarkName;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _saveStatus = "Settings are loaded from the active configuration.";

    [ObservableProperty]
    private string _connectionStatus = "Connection has not been tested.";

    [ObservableProperty]
    private string _connectionHealthState = "Unknown";

    [ObservableProperty]
    private bool _hasIncidents;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    public SettingsViewModel(
        IOptionsMonitor<AIOptions> aiOptions,
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<OpenAIOptions> openAIOptions,
        IOptionsMonitor<FileSystemOptions> fileSystemOptions,
        IOptionsMonitor<HttpOptions> httpOptions,
        IOptionsMonitor<SchedulerOptions> schedulerOptions,
        IOptionsMonitor<SafetyOptions> safetyOptions,
        IOptionsMonitor<LoggingOptions> loggingOptions,
        ILlmClientFactory llmClientFactory,
        ISecretStore secretStore,
        IUserSettingsService userSettingsService,
        IThemeService themeService,
        IIncidentLogService incidentLogService,
        IIncidentReporter incidentReporter,
        IInMemoryLogSink logSink,
        ILogManagementService logManagementService,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger,
        TimeProvider timeProvider)
        : base(messenger)
    {
        _aiOptions = aiOptions;
        _ollamaOptions = ollamaOptions;
        _openAIOptions = openAIOptions;
        _fileSystemOptions = fileSystemOptions;
        _httpOptions = httpOptions;
        _schedulerOptions = schedulerOptions;
        _safetyOptions = safetyOptions;
        _loggingOptions = loggingOptions;
        _llmClientFactory = llmClientFactory;
        _secretStore = secretStore;
        _userSettingsService = userSettingsService;
        _themeService = themeService;
        _incidentLogService = incidentLogService;
        _incidentReporter = incidentReporter;
        _logSink = logSink;
        _logManagementService = logManagementService;
        _desktopInteraction = desktopInteraction;
        _messenger = messenger;
        _timeProvider = timeProvider;

        LoggingLevels = Enum.GetNames<LogEventLevel>();
        ThemeNames = AppThemeCatalog.Names;
        TasksFolder = LocalAppPaths.TasksDirectory;
        ReportsFolder = Path.Combine(
            LocalAppPaths.ApplicationDataDirectory,
            "reports");
        PropertyChanged += OnEditablePropertyChanged;
        IsActive = true;
    }

    /// <summary>
    /// Gets the supported Serilog minimum levels.
    /// </summary>
    public IReadOnlyList<string> LoggingLevels { get; }

    /// <summary>
    /// Gets the supported application theme names.
    /// </summary>
    public IReadOnlyList<string> ThemeNames { get; }

    /// <summary>
    /// Gets the persisted tasks folder.
    /// </summary>
    public string TasksFolder { get; }

    /// <summary>
    /// Gets the generated reports folder.
    /// </summary>
    public string ReportsFolder { get; }

    /// <summary>
    /// Gets the current durable incident list, newest first.
    /// </summary>
    public ObservableCollection<IncidentRecord> Incidents { get; } = [];

    /// <inheritdoc />
    public void Activate()
    {
        if (!_hasLoaded && !LoadCommand.IsRunning)
        {
            LoadCommand.Execute(null);
        }
    }

    /// <inheritdoc />
    public void Receive(IncidentNotificationMessage message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(
            async () => await RefreshIncidentsAsync(CancellationToken.None)
                .ConfigureAwait(true));
    }

    partial void OnIsOllamaProviderChanged(bool value)
    {
        if (value && IsOpenAIProvider)
        {
            IsOpenAIProvider = false;
        }
    }

    partial void OnIsOpenAIProviderChanged(bool value)
    {
        if (value && IsOllamaProvider)
        {
            IsOllamaProvider = false;
        }
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (AppThemeCatalog.TryParse(value, out var theme))
        {
            _themeService.ApplyTheme(theme);
        }
    }

    partial void OnRunnerPathChanged(string value)
    {
        var options = new SchedulerOptions
        {
            RunnerPath = value ?? string.Empty,
            TaskFolder = _schedulerOptions.CurrentValue.TaskFolder,
            RunWhenLoggedOff = RunWhenLoggedOff,
        };
        ResolvedRunnerPath = RunnerPathResolver.Resolve(
            options,
            requireExists: false);
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        _isLoading = true;
        try
        {
            var ai = _aiOptions.CurrentValue;
            var ollama = _ollamaOptions.CurrentValue;
            var openAI = _openAIOptions.CurrentValue;
            var fileSystem = _fileSystemOptions.CurrentValue;
            var http = _httpOptions.CurrentValue;
            var scheduler = _schedulerOptions.CurrentValue;
            var safety = _safetyOptions.CurrentValue;
            var logging = _loggingOptions.CurrentValue;

            IsOllamaProvider = string.Equals(
                ai.Provider,
                AIOptions.OllamaProvider,
                StringComparison.OrdinalIgnoreCase);
            IsOpenAIProvider = !IsOllamaProvider;
            OllamaEndpoint = ollama.Endpoint;
            OllamaModel = ollama.Model;
            OllamaTimeoutSeconds = ollama.TimeoutSeconds;
            OpenAIEndpoint = openAI.Endpoint;
            OpenAIModel = openAI.Model;
            OpenAITimeoutSeconds = openAI.TimeoutSeconds;
            ApiKeyReplacement = string.Empty;
            IsApiKeyConfigured = await _secretStore
                .ExistsAsync(SecretKeys.OpenAIApiKey, cancellationToken)
                .ConfigureAwait(true);
            FileSystemRootPath = fileSystem.RootPath;
            HttpTimeoutSeconds = http.TimeoutSeconds;
            HttpUserAgent = http.UserAgent;
            AllowedHostsText = string.Join(", ", http.AllowedHosts ?? []);
            RunWhenLoggedOff = scheduler.RunWhenLoggedOff;
            RunnerPath = scheduler.RunnerPath;
            ResolvedRunnerPath = RunnerPathResolver.Resolve(
                scheduler,
                requireExists: false);
            AllowSendEmail = safety.AllowSendEmail;
            AllowFileDelete = safety.AllowFileDelete;
            AllowHttpPost = safety.AllowHttpPost;
            LoggingMinimumLevel = logging.MinimumLevel;
            SelectedTheme = AppThemeCatalog.GetName(_themeService.CurrentTheme);
            await RefreshIncidentsAsync(cancellationToken).ConfigureAwait(true);
            SaveStatus = "Settings are loaded from the active configuration.";
            IsDirty = false;
            _hasLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ReportAsync(exception, "Settings load").ConfigureAwait(true);
        }
        finally
        {
            _isLoading = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var currentScheduler = _schedulerOptions.CurrentValue;
            var settings = new UserSettingsData(
                IsOpenAIProvider
                    ? AIOptions.OpenAIProvider
                    : AIOptions.OllamaProvider,
                OllamaEndpoint.Trim(),
                OllamaModel.Trim(),
                OllamaTimeoutSeconds,
                OpenAIEndpoint.Trim(),
                OpenAIModel.Trim(),
                OpenAITimeoutSeconds,
                FileSystemRootPath.Trim(),
                HttpTimeoutSeconds,
                HttpUserAgent.Trim(),
                ParseAllowedHosts(AllowedHostsText),
                currentScheduler.TaskFolder,
                RunWhenLoggedOff,
                RunnerPath.Trim(),
                AllowSendEmail,
                AllowFileDelete,
                AllowHttpPost,
                LoggingMinimumLevel,
                SelectedTheme);
            await _userSettingsService
                .SaveAsync(
                    settings,
                    ApiKeyReplacement,
                    cancellationToken)
                .ConfigureAwait(true);
            IsApiKeyConfigured = await _secretStore
                .ExistsAsync(SecretKeys.OpenAIApiKey, cancellationToken)
                .ConfigureAwait(true);
            _isLoading = true;
            ApiKeyReplacement = string.Empty;
            _isLoading = false;
            IsDirty = false;
            ResolvedRunnerPath = RunnerPathResolver.Resolve(
                new SchedulerOptions
                {
                    TaskFolder = settings.TaskFolder,
                    RunWhenLoggedOff = settings.RunWhenLoggedOff,
                    RunnerPath = settings.RunnerPath,
                },
                requireExists: false);
            SaveStatus =
                "Saved. The selected theme will be restored at the next startup.";
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "Settings saved and reloaded."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException exception)
        {
            SaveStatus = exception.Message;
        }
        catch (Exception exception)
        {
            SaveStatus = exception.Message;
            await ReportAsync(exception, "Settings save").ConfigureAwait(true);
        }
        finally
        {
            _isLoading = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ConnectionHealthState = "Checking";
        ConnectionStatus = "Checking the saved active provider…";
        try
        {
            var provider = _llmClientFactory.Create();
            var result = await provider
                .CheckHealthAsync(cancellationToken)
                .ConfigureAwait(true);
            var healthy = result.IsReachable && result.ModelAvailable;
            ConnectionHealthState = healthy ? "Healthy" : "Unhealthy";
            ConnectionStatus = healthy
                ? $"{provider.ProviderName} is healthy · {result.LatencyMs} ms."
                : $"{provider.ProviderName}: {result.Error ?? "connection failed"} · {result.LatencyMs} ms.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ConnectionStatus = "Connection test canceled.";
            ConnectionHealthState = "Unknown";
        }
        catch (Exception exception)
        {
            ConnectionStatus = exception.Message;
            ConnectionHealthState = "Unhealthy";
            await ReportAsync(exception, "Settings provider connection test")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportLogsAsync(CancellationToken cancellationToken)
    {
        var path = _desktopInteraction.SelectSavePath(
            "Save log buffer",
            "Log files (*.log)|*.log|All files (*.*)|*.*",
            ".log",
            $"OD.MyLocalAgent-{_timeProvider.GetLocalNow():yyyyMMdd-HHmmss}.log");
        if (path is null)
        {
            return;
        }

        try
        {
            await _logManagementService
                .ExportAsync(path, _logSink.GetSnapshot(), cancellationToken)
                .ConfigureAwait(true);
            _messenger.Send(
                new SnackbarNotificationMessage("Log buffer exported."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReportAsync(exception, "Settings log export").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ResetLogsAsync(CancellationToken cancellationToken)
    {
        if (!_desktopInteraction.Confirm(
                "Reset logs",
                "Archive and truncate rolling application logs, then clear the live buffer?"))
        {
            return;
        }

        try
        {
            await _logManagementService
                .ResetAsync(cancellationToken)
                .ConfigureAwait(true);
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "Logs archived and the live buffer was cleared."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReportAsync(exception, "Settings log reset").ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void CopyIncidentDetails(IncidentRecord? incident)
    {
        if (incident is not null)
        {
            _desktopInteraction.CopyText(FormatIncident(incident));
        }
    }

    [RelayCommand]
    private async Task ExportIncidentsAsync(CancellationToken cancellationToken)
    {
        var path = _desktopInteraction.SelectSavePath(
            "Export incidents",
            "JSON Lines files (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            ".jsonl",
            $"incidents-{_timeProvider.GetLocalNow():yyyyMMdd-HHmmss}.jsonl");
        if (path is null)
        {
            return;
        }

        try
        {
            await _incidentLogService
                .ExportAsync(path, cancellationToken)
                .ConfigureAwait(true);
            _messenger.Send(
                new SnackbarNotificationMessage("Incidents exported."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReportAsync(exception, "Settings incident export")
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task ClearIncidentsAsync(CancellationToken cancellationToken)
    {
        if (!_desktopInteraction.Confirm(
                "Clear incidents",
                "Permanently clear all persisted incident records?"))
        {
            return;
        }

        try
        {
            await _incidentLogService
                .ClearAsync(cancellationToken)
                .ConfigureAwait(true);
            Incidents.Clear();
            HasIncidents = false;
            _messenger.Send(
                new SnackbarNotificationMessage("Incident history cleared."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReportAsync(exception, "Settings clear incidents")
                .ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void TriggerTestIncident()
    {
        throw new InvalidOperationException(
            "This is a deliberate test incident from Settings diagnostics.");
    }

    private async Task RefreshIncidentsAsync(
        CancellationToken cancellationToken)
    {
        var incidents = await _incidentLogService
            .ReadAsync(cancellationToken)
            .ConfigureAwait(true);
        Incidents.Clear();
        foreach (var incident in incidents.Reverse())
        {
            Incidents.Add(incident);
        }

        HasIncidents = Incidents.Count > 0;
    }

    private void OnEditablePropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!_isLoading &&
            e.PropertyName is not null &&
            EditablePropertyNames.Contains(e.PropertyName))
        {
            IsDirty = true;
            SaveStatus = "Unsaved changes.";
        }
    }

    private async Task ReportAsync(Exception exception, string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }

    private static IReadOnlyList<string> ParseAllowedHosts(string text) =>
        text.Split(
                ',',
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string FormatIncident(IncidentRecord incident) =>
        "Timestamp (UTC): " +
        incident.TimestampUtc.ToString("O", CultureInfo.InvariantCulture) +
        Environment.NewLine +
        $"Severity: {incident.Severity}{Environment.NewLine}" +
        $"Source: {incident.Source}{Environment.NewLine}" +
        $"Correlation id: {incident.CorrelationId}{Environment.NewLine}" +
        $"Message: {incident.Message}{Environment.NewLine}{Environment.NewLine}" +
        incident.StackTrace;
}
