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
using OD.MyLocalAgent.Guardrails;
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
    IActivatablePage,
    IAsyncNavigationGuard
{
    private const int ModelCatalogTimeoutSeconds = 10;
    private const string ApplicationExecutableFileName = "OD.MyLocalAgent.UI.exe";

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
        nameof(GenerationTemperatureText),
        nameof(GenerationTopPText),
        nameof(ContextWindowTokensText),
        nameof(GlobalSystemPrompt),
        nameof(ApiKeyReplacement),
        nameof(FileSystemRootPath),
        nameof(HttpTimeoutSeconds),
        nameof(HttpUserAgent),
        nameof(AllowedHostsText),
        nameof(RunWhenLoggedOff),
        nameof(RunnerPath),
        nameof(FileContentMaxInputFileBytes),
        nameof(FileContentMaxArchiveEntries),
        nameof(FileContentMaxArchiveEntryBytes),
        nameof(FileContentMaxArchiveTotalBytes),
        nameof(FileContentMaxArchiveCompressionRatio),
        nameof(FileContentMaxListedEntries),
        nameof(FileContentMaxDocumentCharacters),
        nameof(FileContentMaxSpreadsheetRows),
        nameof(FileContentMaxSpreadsheetColumns),
        nameof(FileContentMaxSpreadsheetCells),
        nameof(FileContentMaxPdfPages),
        nameof(SqliteDefaultMaxRows),
        nameof(SqliteMaxRows),
        nameof(SqliteMaxResultCharacters),
        nameof(SqliteMaxBlobBytes),
        nameof(SqliteMaxSqlCharacters),
        nameof(SqliteCommandTimeoutSeconds),
        nameof(SqliteGuardrailsEnabledByDefault),
        nameof(GuardrailsScreeningEnabled),
        nameof(GuardrailsCleanThreshold),
        nameof(GuardrailsCautionThreshold),
        nameof(GuardrailsMaxContentChars),
        nameof(GuardrailsSummarizeOversizedContent),
        nameof(GuardrailsMaxSummaryChars),
        nameof(GuardrailsMaxSummarizationSourceChars),
        nameof(GuardrailsTimeoutSeconds),
        nameof(GuardrailsCacheResults),
        nameof(GuardrailsMaxGmailMessagesPerOperation),
        nameof(LoggingMinimumLevel),
        nameof(SelectedTheme),
    ];

    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<OpenAIOptions> _openAIOptions;
    private readonly IOptionsMonitor<FileSystemOptions> _fileSystemOptions;
    private readonly IOptionsMonitor<HttpOptions> _httpOptions;
    private readonly IOptionsMonitor<SchedulerOptions> _schedulerOptions;
    private readonly IOptionsMonitor<GuardrailsOptions> _guardrailsOptions;
    private readonly IOptionsMonitor<LoggingOptions> _loggingOptions;
    private readonly IOptionsMonitor<SQLiteOptions>? _sqliteOptions;
    private readonly IOptionsMonitor<FileContentOptions>? _fileContentOptions;
    private readonly ILlmClientFactory _llmClientFactory;
    private readonly IOllamaModelCatalog _ollamaModelCatalog;
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
    private string _savedTheme = AppThemeCatalog.DarkName;

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
    private bool _isLoadingOllamaModels;

    [ObservableProperty]
    private string _ollamaModelCatalogStatus =
        "Installed models have not been loaded.";

    [ObservableProperty]
    private string _openAIEndpoint = string.Empty;

    [ObservableProperty]
    private string _openAIModel = string.Empty;

    [ObservableProperty]
    private int _openAITimeoutSeconds;

    [ObservableProperty]
    private string _generationTemperatureText = "0.2";

    [ObservableProperty]
    private string _generationTopPText = "1";

    [ObservableProperty]
    private string _contextWindowTokensText = "8192";

    [ObservableProperty]
    private string _globalSystemPrompt = string.Empty;

    [ObservableProperty]
    private string _generationValidationText =
        "Generation defaults are valid.";

    [ObservableProperty]
    private bool _hasGenerationValidationError;

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

    [ObservableProperty] private long _fileContentMaxInputFileBytes = 104857600;
    [ObservableProperty] private int _fileContentMaxArchiveEntries = 10000;
    [ObservableProperty] private long _fileContentMaxArchiveEntryBytes = 262144000;
    [ObservableProperty] private long _fileContentMaxArchiveTotalBytes = 1073741824;
    [ObservableProperty] private int _fileContentMaxArchiveCompressionRatio = 200;
    [ObservableProperty] private int _fileContentMaxListedEntries = 1000;
    [ObservableProperty] private int _fileContentMaxDocumentCharacters = 64000;
    [ObservableProperty] private int _fileContentMaxSpreadsheetRows = 1000;
    [ObservableProperty] private int _fileContentMaxSpreadsheetColumns = 256;
    [ObservableProperty] private int _fileContentMaxSpreadsheetCells = 25000;
    [ObservableProperty] private int _fileContentMaxPdfPages = 100;

    [ObservableProperty] private int _sqliteDefaultMaxRows = 100;
    [ObservableProperty] private int _sqliteMaxRows = 1_000;
    [ObservableProperty] private int _sqliteMaxResultCharacters = 64_000;
    [ObservableProperty] private int _sqliteMaxBlobBytes = 4_096;
    [ObservableProperty] private int _sqliteMaxSqlCharacters = 16_000;
    [ObservableProperty] private int _sqliteCommandTimeoutSeconds = 30;
    [ObservableProperty] private bool _sqliteGuardrailsEnabledByDefault = true;
    [ObservableProperty] private string _sqliteValidationText = "SQLite settings are valid.";
    [ObservableProperty] private bool _hasSqliteValidationError;

    [ObservableProperty]
    private bool _guardrailsScreeningEnabled = true;

    [ObservableProperty]
    private int _guardrailsCleanThreshold = 80;

    [ObservableProperty]
    private int _guardrailsCautionThreshold = 50;

    [ObservableProperty]
    private int _guardrailsMaxContentChars = 8_000;

    [ObservableProperty]
    private bool _guardrailsSummarizeOversizedContent = true;

    [ObservableProperty]
    private int _guardrailsMaxSummaryChars = 4_000;

    [ObservableProperty]
    private int _guardrailsMaxSummarizationSourceChars = 100_000;

    [ObservableProperty]
    private int _guardrailsTimeoutSeconds = 60;

    [ObservableProperty]
    private bool _guardrailsCacheResults = true;

    [ObservableProperty]
    private int _guardrailsMaxGmailMessagesPerOperation;

    [ObservableProperty]
    private string _loggingMinimumLevel = nameof(LogEventLevel.Information);

    [ObservableProperty]
    private string _selectedTheme = AppThemeCatalog.DarkName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
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
        IOptionsMonitor<GuardrailsOptions> guardrailsOptions,
        IOptionsMonitor<LoggingOptions> loggingOptions,
        ILlmClientFactory llmClientFactory,
        IOllamaModelCatalog ollamaModelCatalog,
        ISecretStore secretStore,
        IUserSettingsService userSettingsService,
        IThemeService themeService,
        IIncidentLogService incidentLogService,
        IIncidentReporter incidentReporter,
        IInMemoryLogSink logSink,
        ILogManagementService logManagementService,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger,
        TimeProvider timeProvider,
        IOptionsMonitor<SQLiteOptions>? sqliteOptions = null,
        IOptionsMonitor<FileContentOptions>? fileContentOptions = null)
        : base(messenger)
    {
        _aiOptions = aiOptions;
        _ollamaOptions = ollamaOptions;
        _openAIOptions = openAIOptions;
        _fileSystemOptions = fileSystemOptions;
        _httpOptions = httpOptions;
        _schedulerOptions = schedulerOptions;
        _guardrailsOptions = guardrailsOptions;
        _loggingOptions = loggingOptions;
        _sqliteOptions = sqliteOptions;
        _fileContentOptions = fileContentOptions;
        _llmClientFactory = llmClientFactory;
        _ollamaModelCatalog = ollamaModelCatalog;
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
        ExecutableFolder = Path.GetFullPath(AppContext.BaseDirectory);
        ApplicationExecutablePath = Path.Combine(
            ExecutableFolder,
            ApplicationExecutableFileName);
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
    /// Gets the model identifiers currently installed on the configured Ollama endpoint.
    /// </summary>
    public ObservableCollection<string> OllamaModels { get; } = [];

    /// <summary>
    /// Gets the persisted tasks folder.
    /// </summary>
    public string TasksFolder { get; }

    /// <summary>
    /// Gets the generated reports folder.
    /// </summary>
    public string ReportsFolder { get; }

    /// <summary>
    /// Gets the directory containing the running application executable.
    /// </summary>
    public string ExecutableFolder { get; }

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
    public async Task<bool> CanNavigateAwayAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        switch (_desktopInteraction.PromptUnsavedChanges(
                    "Unsaved settings",
                    "Save your changes before leaving Settings?"))
        {
            case UnsavedChangesChoice.Save:
                await SaveAsync(CancellationToken.None).ConfigureAwait(true);
                return !IsDirty;
            case UnsavedChangesChoice.Discard:
                DiscardChangesCore();
                return true;
            default:
                return false;
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

    [RelayCommand]
    private void CopyReportsFolder()
    {
        _desktopInteraction.CopyText(ReportsFolder);
        _messenger.Send(
            new SnackbarNotificationMessage(
                "Reports folder copied to the clipboard."));
    }

    [RelayCommand]
    private void CopyExecutableFolder()
    {
        _desktopInteraction.CopyText(ExecutableFolder);
        _messenger.Send(
            new SnackbarNotificationMessage(
                "Application executable folder copied to the clipboard."));
    }

    [RelayCommand]
    private async Task CreateDesktopShortcutAsync()
    {
        try
        {
            var shortcutPath = _desktopInteraction.CreateDesktopShortcut(
                "OD.MyLocalAgent",
                ApplicationExecutablePath);
            _messenger.Send(
                new SnackbarNotificationMessage(
                    $"Desktop shortcut created: {shortcutPath}"));
        }
        catch (Exception exception)
        {
            await ReportAsync(
                    exception,
                    "Settings desktop shortcut creation")
                .ConfigureAwait(true);
        }
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

    partial void OnGenerationTemperatureTextChanged(string value) =>
        UpdateGenerationValidation();

    partial void OnGenerationTopPTextChanged(string value) =>
        UpdateGenerationValidation();

    partial void OnContextWindowTokensTextChanged(string value) =>
        UpdateGenerationValidation();

    partial void OnSqliteDefaultMaxRowsChanged(int value) => UpdateSqliteValidation();

    partial void OnSqliteMaxRowsChanged(int value) => UpdateSqliteValidation();

    partial void OnSqliteMaxResultCharactersChanged(int value) => UpdateSqliteValidation();

    partial void OnSqliteMaxBlobBytesChanged(int value) => UpdateSqliteValidation();

    partial void OnSqliteMaxSqlCharactersChanged(int value) => UpdateSqliteValidation();

    partial void OnSqliteCommandTimeoutSecondsChanged(int value) => UpdateSqliteValidation();

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
            var guardrails = _guardrailsOptions.CurrentValue;
            var logging = _loggingOptions.CurrentValue;
            var sqlite = _sqliteOptions?.CurrentValue ?? new SQLiteOptions();
            var fileContent = _fileContentOptions?.CurrentValue ?? new FileContentOptions();

            IsOllamaProvider = string.Equals(
                ai.Provider,
                AIOptions.OllamaProvider,
                StringComparison.OrdinalIgnoreCase);
            IsOpenAIProvider = !IsOllamaProvider;
            GenerationTemperatureText = ai.GenerationDefaults.Temperature.ToString(
                "G",
                CultureInfo.CurrentCulture);
            GenerationTopPText = ai.GenerationDefaults.TopP.ToString(
                "G",
                CultureInfo.CurrentCulture);
            ContextWindowTokensText =
                ai.GenerationDefaults.ContextWindowTokens.ToString(
                    CultureInfo.CurrentCulture);
            GlobalSystemPrompt = ai.GlobalSystemPrompt;
            UpdateGenerationValidation();
            OllamaEndpoint = ollama.Endpoint;
            OllamaModel = ollama.Model;
            OllamaTimeoutSeconds = ollama.TimeoutSeconds;
            await LoadOllamaModelsAsync(cancellationToken).ConfigureAwait(true);
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
            FileContentMaxInputFileBytes = fileContent.MaxInputFileBytes;
            FileContentMaxArchiveEntries = fileContent.MaxArchiveEntries;
            FileContentMaxArchiveEntryBytes = fileContent.MaxArchiveEntryBytes;
            FileContentMaxArchiveTotalBytes = fileContent.MaxArchiveTotalBytes;
            FileContentMaxArchiveCompressionRatio = fileContent.MaxArchiveCompressionRatio;
            FileContentMaxListedEntries = fileContent.MaxListedEntries;
            FileContentMaxDocumentCharacters = fileContent.MaxDocumentCharacters;
            FileContentMaxSpreadsheetRows = fileContent.MaxSpreadsheetRows;
            FileContentMaxSpreadsheetColumns = fileContent.MaxSpreadsheetColumns;
            FileContentMaxSpreadsheetCells = fileContent.MaxSpreadsheetCells;
            FileContentMaxPdfPages = fileContent.MaxPdfPages;
            SqliteDefaultMaxRows = sqlite.DefaultMaxRows;
            SqliteMaxRows = sqlite.MaxRows;
            SqliteMaxResultCharacters = sqlite.MaxResultCharacters;
            SqliteMaxBlobBytes = sqlite.MaxBlobBytes;
            SqliteMaxSqlCharacters = sqlite.MaxSqlCharacters;
            SqliteCommandTimeoutSeconds = sqlite.CommandTimeoutSeconds;
            SqliteGuardrailsEnabledByDefault = sqlite.GuardrailsEnabledByDefault;
            UpdateSqliteValidation();
            GuardrailsScreeningEnabled = guardrails.ScreeningEnabled;
            GuardrailsCleanThreshold = guardrails.CleanThreshold;
            GuardrailsCautionThreshold = guardrails.CautionThreshold;
            GuardrailsMaxContentChars = guardrails.MaxContentChars;
            GuardrailsSummarizeOversizedContent =
                guardrails.SummarizeOversizedContent;
            GuardrailsMaxSummaryChars = guardrails.MaxSummaryChars;
            GuardrailsMaxSummarizationSourceChars =
                guardrails.MaxSummarizationSourceChars;
            GuardrailsTimeoutSeconds = guardrails.TimeoutSeconds;
            GuardrailsCacheResults = guardrails.CacheResults;
            GuardrailsMaxGmailMessagesPerOperation =
                guardrails.MaxGmailMessagesPerOperation;
            LoggingMinimumLevel = logging.MinimumLevel;
            SelectedTheme = AppThemeCatalog.GetName(_themeService.CurrentTheme);
            await RefreshIncidentsAsync(cancellationToken).ConfigureAwait(true);
            SaveStatus = "Settings are loaded from the active configuration.";
            IsDirty = false;
            _savedTheme = SelectedTheme;
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

    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
    private async Task DiscardChangesAsync()
    {
        if (_desktopInteraction.Confirm(
                "Discard settings changes",
                "Discard all changes made since Settings was last saved?"))
        {
            DiscardChangesCore();
            await LoadAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task RefreshOllamaModelsAsync(CancellationToken cancellationToken) =>
        LoadOllamaModelsAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            if (!TryGetGenerationDefaults(out var generationDefaults))
            {
                UpdateGenerationValidation();
                return;
            }

            if (!TryGetSqliteOptions(out _))
            {
                UpdateSqliteValidation();
                return;
            }

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
                GuardrailsScreeningEnabled,
                GuardrailsCleanThreshold,
                GuardrailsCautionThreshold,
                GuardrailsMaxContentChars,
                GuardrailsSummarizeOversizedContent,
                GuardrailsMaxSummaryChars,
                GuardrailsMaxSummarizationSourceChars,
                GuardrailsTimeoutSeconds,
                GuardrailsCacheResults,
                GuardrailsMaxGmailMessagesPerOperation,
                LoggingMinimumLevel,
                SelectedTheme,
                generationDefaults.Temperature,
                generationDefaults.TopP,
                generationDefaults.ContextWindowTokens,
                SqliteDefaultMaxRows,
                SqliteMaxRows,
                SqliteMaxResultCharacters,
                SqliteMaxBlobBytes,
                SqliteMaxSqlCharacters,
                SqliteCommandTimeoutSeconds,
                SqliteGuardrailsEnabledByDefault,
                GlobalSystemPrompt.Trim(),
                FileContentMaxInputFileBytes,
                FileContentMaxArchiveEntries,
                FileContentMaxArchiveEntryBytes,
                FileContentMaxArchiveTotalBytes,
                FileContentMaxArchiveCompressionRatio,
                FileContentMaxListedEntries,
                FileContentMaxDocumentCharacters,
                FileContentMaxSpreadsheetRows,
                FileContentMaxSpreadsheetColumns,
                FileContentMaxSpreadsheetCells,
                FileContentMaxPdfPages);
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
            _savedTheme = SelectedTheme;
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
            _messenger.Send(new ProviderConfigurationChangedMessage());
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

    private async Task LoadOllamaModelsAsync(CancellationToken cancellationToken)
    {
        IsLoadingOllamaModels = true;
        OllamaModelCatalogStatus = "Loading installed models from Ollama…";
        try
        {
            var models = await _ollamaModelCatalog
                .ListInstalledModelsAsync(
                    OllamaEndpoint.Trim(),
                    Math.Clamp(
                        OllamaTimeoutSeconds,
                        1,
                        ModelCatalogTimeoutSeconds),
                    cancellationToken)
                .ConfigureAwait(true);
            OllamaModels.Clear();
            foreach (var model in models)
            {
                OllamaModels.Add(model);
            }

            OllamaModelCatalogStatus = models.Count switch
            {
                0 => "Ollama is reachable, but no installed model was found.",
                1 => "1 installed model available.",
                _ => $"{models.Count} installed models available.",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OllamaModelCatalogStatus = "Loading installed models was canceled.";
            throw;
        }
        catch (Exception exception)
        {
            OllamaModels.Clear();
            OllamaModelCatalogStatus =
                $"Installed models could not be loaded: {exception.Message}";
        }
        finally
        {
            IsLoadingOllamaModels = false;
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
            SaveStatus = HasGenerationValidationError || HasSqliteValidationError
                ? "Fix the validation errors before saving."
                : "Unsaved changes.";
        }
    }

    private bool CanSave() =>
        IsDirty && !IsBusy && TryGetGenerationDefaults(out _) &&
        TryGetSqliteOptions(out _);

    private bool CanDiscardChanges() => IsDirty && !IsBusy;

    private void DiscardChangesCore()
    {
        _isLoading = true;
        try
        {
            IsDirty = false;
            SaveStatus = "Changes discarded.";
            _hasLoaded = false;
            if (AppThemeCatalog.TryParse(_savedTheme, out var theme))
            {
                _themeService.ApplyTheme(theme);
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateGenerationValidation()
    {
        if (TryGetGenerationDefaults(out _))
        {
            HasGenerationValidationError = false;
            GenerationValidationText =
                "Valid. New calls use these values after Save; running calls keep their current policy.";
        }
        else
        {
            HasGenerationValidationError = true;
        }

        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetGenerationDefaults(
        out AIGenerationDefaults defaults)
    {
        defaults = new AIGenerationDefaults();
        if (!TryParseCultureSafeDouble(
                GenerationTemperatureText,
                CultureInfo.CurrentCulture,
                out var temperature))
        {
            GenerationValidationText =
                "Temperature must be a number from 0 through 2.";
            return false;
        }

        if (!TryParseCultureSafeDouble(
                GenerationTopPText,
                CultureInfo.CurrentCulture,
                out var topP))
        {
            GenerationValidationText =
                "Top P must be a number greater than 0 and at most 1.";
            return false;
        }

        if (!int.TryParse(
                ContextWindowTokensText,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var contextWindowTokens) &&
            !int.TryParse(
                ContextWindowTokensText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out contextWindowTokens))
        {
            GenerationValidationText =
                "Context window must be a whole number of tokens.";
            return false;
        }

        defaults = new AIGenerationDefaults
        {
            Temperature = temperature,
            TopP = topP,
            ContextWindowTokens = contextWindowTokens,
        };
        var error = GenerationPolicyValidation.ValidateDefaults(defaults);
        if (error is not null)
        {
            GenerationValidationText = error;
            return false;
        }

        return true;
    }

    private void UpdateSqliteValidation()
    {
        HasSqliteValidationError = !TryGetSqliteOptions(out _);
        if (!HasSqliteValidationError)
        {
            SqliteValidationText = "SQLite settings are valid.";
        }

        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetSqliteOptions(out SQLiteOptions options)
    {
        options = new SQLiteOptions
        {
            DefaultMaxRows = SqliteDefaultMaxRows,
            MaxRows = SqliteMaxRows,
            MaxResultCharacters = SqliteMaxResultCharacters,
            MaxBlobBytes = SqliteMaxBlobBytes,
            MaxSqlCharacters = SqliteMaxSqlCharacters,
            CommandTimeoutSeconds = SqliteCommandTimeoutSeconds,
            GuardrailsEnabledByDefault = SqliteGuardrailsEnabledByDefault,
        };

        if (options.DefaultMaxRows < 1 || options.DefaultMaxRows > options.MaxRows)
        {
            SqliteValidationText = "Default maximum rows must be at least 1 and no greater than maximum rows per call.";
            return false;
        }

        if (options.MaxRows is < 1 or > 10_000)
        {
            SqliteValidationText = "Maximum rows per call must be from 1 through 10000.";
            return false;
        }

        if (options.MaxResultCharacters is < 4_096 or > 1_000_000)
        {
            SqliteValidationText = "Maximum result characters must be from 4096 through 1000000.";
            return false;
        }

        if (options.MaxBlobBytes is < 0 or > 1_000_000)
        {
            SqliteValidationText = "Maximum BLOB bytes must be from 0 through 1000000.";
            return false;
        }

        if (options.MaxSqlCharacters is < 256 or > 1_000_000)
        {
            SqliteValidationText = "Maximum SQL characters must be from 256 through 1000000.";
            return false;
        }

        if (options.CommandTimeoutSeconds is < 1 or > 300)
        {
            SqliteValidationText = "Command timeout must be from 1 through 300 seconds.";
            return false;
        }

        return true;
    }

    internal static bool TryParseCultureSafeDouble(
        string text,
        CultureInfo culture,
        out double value)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var candidate = text?.Trim() ?? string.Empty;
        if (double.TryParse(
                candidate,
                NumberStyles.Float,
                culture,
                out value) ||
            double.TryParse(
                candidate,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return double.IsFinite(value);
        }

        var normalized = candidate.Replace(',', '.');
        return double.TryParse(
                   normalized,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out value) &&
               double.IsFinite(value);
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

    private string ApplicationExecutablePath { get; }

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
