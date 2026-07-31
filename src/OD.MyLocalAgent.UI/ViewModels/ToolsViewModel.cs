using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Manages Google authorization, Dropbox and Web Search secrets, and connector health checks.
/// </summary>
public sealed partial class ToolsViewModel :
    ObservableObject,
    IActivatablePage
{
    private static readonly TimeSpan ConnectorTestTimeout = TimeSpan.FromSeconds(15);

    private readonly IGoogleCredentialProvider _googleCredentialProvider;
    private readonly IGmailConnector _gmailConnector;
    private readonly ICalendarConnector _calendarConnector;
    private readonly IDropboxConnector _dropboxConnector;
    private readonly IWebSearchConnector _webSearchConnector;
    private readonly IOptionsMonitor<WebSearchOptions> _webSearchOptions;
    private readonly ISecretStore _secretStore;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IConfiguration _configuration;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _timeProvider;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isGoogleConnected;

    [ObservableProperty]
    private string _googleOAuthStatus = "Google authorization has not been inspected.";

    [ObservableProperty]
    private string _googleConfigurationStatus = string.Empty;

    [ObservableProperty]
    private string _googleScopesText = string.Empty;

    [ObservableProperty]
    private string _dropboxAccessTokenReplacement = string.Empty;

    [ObservableProperty]
    private string _dropboxRefreshTokenReplacement = string.Empty;

    [ObservableProperty]
    private string _dropboxAppKeyReplacement = string.Empty;

    [ObservableProperty]
    private bool _isDropboxConfigured;

    [ObservableProperty]
    private string _dropboxMaskedStatus = "Not configured";

    [ObservableProperty]
    private string _gmailTestStatus = "Not tested";

    [ObservableProperty]
    private string _gmailTestState = "Unknown";

    [ObservableProperty]
    private string _calendarTestStatus = "Not tested";

    [ObservableProperty]
    private string _calendarTestState = "Unknown";

    [ObservableProperty]
    private string _dropboxTestStatus = "Not tested";

    [ObservableProperty]
    private string _dropboxTestState = "Unknown";

    [ObservableProperty]
    private bool _isWebSearchEnabled;

    [ObservableProperty]
    private string _webSearchEndpoint =
        "https://ollama.com/api/web_search";

    [ObservableProperty]
    private string _webSearchApiKeyReplacement = string.Empty;

    [ObservableProperty]
    private bool _isWebSearchConfigured;

    [ObservableProperty]
    private string _webSearchMaskedStatus = "Not configured";

    [ObservableProperty]
    private string _webSearchTestStatus = "Not tested";

    [ObservableProperty]
    private string _webSearchTestState = "Unknown";

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsViewModel"/> class.
    /// </summary>
    public ToolsViewModel(
        IGoogleCredentialProvider googleCredentialProvider,
        IGmailConnector gmailConnector,
        ICalendarConnector calendarConnector,
        IDropboxConnector dropboxConnector,
        IWebSearchConnector webSearchConnector,
        IOptionsMonitor<WebSearchOptions> webSearchOptions,
        ISecretStore secretStore,
        IUserSettingsService userSettingsService,
        IConfiguration configuration,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger,
        TimeProvider timeProvider)
    {
        _googleCredentialProvider = googleCredentialProvider;
        _gmailConnector = gmailConnector;
        _calendarConnector = calendarConnector;
        _dropboxConnector = dropboxConnector;
        _webSearchConnector = webSearchConnector;
        _webSearchOptions = webSearchOptions;
        _secretStore = secretStore;
        _userSettingsService = userSettingsService;
        _configuration = configuration;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _messenger = messenger;
        _timeProvider = timeProvider;
        GoogleScopesText = string.Join(
            Environment.NewLine,
            GoogleCredentialProvider.RequiredScopes.Select(
                static scope => $"• {scope}"));
    }

    /// <inheritdoc />
    public void Activate()
    {
        if (!RefreshCommand.IsRunning)
        {
            RefreshCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var clientId = _configuration["Google:ClientId"];
            var clientSecret = _configuration["Google:ClientSecret"];
            var googleConfigured =
                !IsMissingOrPlaceholder(clientId) &&
                !IsMissingOrPlaceholder(clientSecret);
            GoogleConfigurationStatus = googleConfigured
                ? "OAuth desktop client id and secret are configured."
                : "Set Google:ClientId and Google:ClientSecret in appsettings.json before connecting.";
            IsGoogleConnected =
                Directory.Exists(GoogleCredentialProvider.CredentialStorePath) &&
                Directory.EnumerateFiles(
                    GoogleCredentialProvider.CredentialStorePath,
                    "*",
                    SearchOption.AllDirectories).Any();
            GoogleOAuthStatus = IsGoogleConnected
                ? $"Connected · credential store: {GoogleCredentialProvider.CredentialStorePath}"
                : $"Not connected · no credential in {GoogleCredentialProvider.CredentialStorePath}";

            var hasAccessToken = await _secretStore
                .ExistsAsync(
                    SecretKeys.DropboxAccessToken,
                    cancellationToken)
                .ConfigureAwait(true);
            var hasRefreshToken = await _secretStore
                .ExistsAsync(
                    SecretKeys.DropboxRefreshToken,
                    cancellationToken)
                .ConfigureAwait(true);
            var hasAppKey = await _secretStore
                .ExistsAsync(
                    SecretKeys.DropboxAppKey,
                    cancellationToken)
                .ConfigureAwait(true);
            IsDropboxConfigured =
                hasAccessToken || (hasRefreshToken && hasAppKey);
            DropboxMaskedStatus = IsDropboxConfigured
                ? "•••••••• · stored with Windows DPAPI"
                : "Not configured · enter an access token, or refresh token and app key.";

            var webSearch = _webSearchOptions.CurrentValue;
            IsWebSearchEnabled = string.Equals(
                webSearch.Provider,
                WebSearchOptions.OllamaProvider,
                StringComparison.OrdinalIgnoreCase);
            WebSearchEndpoint = webSearch.Endpoint;
            IsWebSearchConfigured = await _secretStore
                .ExistsAsync(
                    SecretKeys.WebSearchApiKey,
                    cancellationToken)
                .ConfigureAwait(true);
            WebSearchMaskedStatus = IsWebSearchConfigured
                ? "Configured: yes · •••••••• · stored with Windows DPAPI"
                : "Configured: no · add an Ollama Web Search API key.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ReportAsync(exception, "Tools configuration refresh")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectGoogleAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        GoogleOAuthStatus =
            "Waiting for Google consent in the browser. Return here after approval.";
        try
        {
            _ = await _googleCredentialProvider
                .AuthorizeInteractiveAsync(cancellationToken)
                .ConfigureAwait(true);
            IsGoogleConnected = true;
            GoogleOAuthStatus =
                $"Connected · credential store: {GoogleCredentialProvider.CredentialStorePath}";
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "Google authorization completed."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            GoogleOAuthStatus = "Google authorization canceled.";
        }
        catch (ConnectorConfigurationException exception)
        {
            IsGoogleConnected = false;
            GoogleOAuthStatus = exception.Message;
        }
        catch (Exception exception)
        {
            IsGoogleConnected = false;
            GoogleOAuthStatus = FormatTypedError(exception);
            await ReportAsync(exception, "Google interactive authorization")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectGoogleAsync(CancellationToken cancellationToken)
    {
        if (!_desktopInteraction.Confirm(
                "Disconnect Google",
                "Remove the persisted Google OAuth credential for this Windows account?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _googleCredentialProvider
                .DisconnectAsync(cancellationToken)
                .ConfigureAwait(true);
            IsGoogleConnected = false;
            GoogleOAuthStatus = "Not connected. Persisted Google authorization was removed.";
            GmailTestState = "Unknown";
            GmailTestStatus = "Not tested";
            CalendarTestState = "Unknown";
            CalendarTestStatus = "Not tested";
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "Google authorization removed."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            GoogleOAuthStatus = FormatTypedError(exception);
            await ReportAsync(exception, "Google disconnect").ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveDropboxAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var changed = false;
            changed |= await SaveSecretReplacementAsync(
                    SecretKeys.DropboxAccessToken,
                    DropboxAccessTokenReplacement,
                    cancellationToken)
                .ConfigureAwait(true);
            changed |= await SaveSecretReplacementAsync(
                    SecretKeys.DropboxRefreshToken,
                    DropboxRefreshTokenReplacement,
                    cancellationToken)
                .ConfigureAwait(true);
            changed |= await SaveSecretReplacementAsync(
                    SecretKeys.DropboxAppKey,
                    DropboxAppKeyReplacement,
                    cancellationToken)
                .ConfigureAwait(true);
            if (!changed)
            {
                DropboxMaskedStatus =
                    "Enter at least one replacement value before saving.";
                return;
            }

            ReloadConfiguration();
            DropboxAccessTokenReplacement = string.Empty;
            DropboxRefreshTokenReplacement = string.Empty;
            DropboxAppKeyReplacement = string.Empty;
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "Dropbox credentials saved with Windows DPAPI."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DropboxMaskedStatus = FormatTypedError(exception);
            await ReportAsync(exception, "Dropbox credential save")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearDropboxAsync(CancellationToken cancellationToken)
    {
        if (!_desktopInteraction.Confirm(
                "Clear Dropbox credentials",
                "Remove all DPAPI-protected Dropbox credentials?"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var key in new[]
                     {
                         SecretKeys.DropboxAccessToken,
                         SecretKeys.DropboxRefreshToken,
                         SecretKeys.DropboxAppKey,
                     })
            {
                _ = await _secretStore
                    .RemoveAsync(key, cancellationToken)
                    .ConfigureAwait(true);
                _configuration[key] = null;
            }

            ReloadConfiguration();
            IsDropboxConfigured = false;
            DropboxMaskedStatus = "Not configured";
            DropboxTestState = "Unknown";
            DropboxTestStatus = "Not tested";
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "Dropbox credentials removed."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DropboxMaskedStatus = FormatTypedError(exception);
            await ReportAsync(exception, "Dropbox credential clear")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveWebSearchAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var provider = IsWebSearchEnabled
                ? WebSearchOptions.OllamaProvider
                : string.Empty;
            await _userSettingsService
                .SaveWebSearchAsync(
                    provider,
                    WebSearchEndpoint,
                    WebSearchApiKeyReplacement,
                    cancellationToken)
                .ConfigureAwait(true);
            WebSearchApiKeyReplacement = string.Empty;
            WebSearchTestState = "Unknown";
            WebSearchTestStatus = "Not tested";
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "Web Search configuration saved."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            WebSearchMaskedStatus = FormatTypedError(exception);
            await ReportAsync(exception, "Web Search configuration save")
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TestWebSearchAsync(CancellationToken cancellationToken)
    {
        WebSearchTestState = "Checking";
        WebSearchTestStatus = "Testing Ollama Web Search cloud access…";
        var started = _timeProvider.GetTimestamp();
        try
        {
            var options = _webSearchOptions.CurrentValue;
            var result = await _webSearchConnector
                .SearchAsync(
                    "ollama web search",
                    options.DefaultMaxResults,
                    cancellationToken)
                .ConfigureAwait(true);
            WebSearchTestState = "Healthy";
            WebSearchTestStatus =
                $"Connected · {ElapsedMilliseconds(started):F0} ms · {result.Items.Count} result(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WebSearchTestState = "Unknown";
            WebSearchTestStatus = "Test canceled.";
        }
        catch (Exception exception)
        {
            WebSearchTestState = "Unhealthy";
            WebSearchTestStatus =
                $"{FormatTypedError(exception)} · {ElapsedMilliseconds(started):F0} ms";
        }
    }

    [RelayCommand]
    private async Task TestGmailAsync(CancellationToken cancellationToken)
    {
        GmailTestState = "Checking";
        GmailTestStatus = "Testing Gmail read access…";
        var started = _timeProvider.GetTimestamp();
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ConnectorTestTimeout);
        try
        {
            await _gmailConnector
                .CheckReadAccessAsync(timeoutSource.Token)
                .ConfigureAwait(true);
            GmailTestState = "Healthy";
            GmailTestStatus =
                $"Connected · {ElapsedMilliseconds(started):F0} ms";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            GmailTestState = "Unknown";
            GmailTestStatus = "Test canceled.";
        }
        catch (OperationCanceledException)
        {
            GmailTestState = "Unhealthy";
            GmailTestStatus =
                $"Gmail read test timed out after {ConnectorTestTimeout.TotalSeconds:F0} seconds.";
        }
        catch (Exception exception)
        {
            GmailTestState = "Unhealthy";
            GmailTestStatus =
                $"{FormatTypedError(exception)} · {ElapsedMilliseconds(started):F0} ms";
        }
    }

    [RelayCommand]
    private async Task TestCalendarAsync(CancellationToken cancellationToken)
    {
        CalendarTestState = "Checking";
        CalendarTestStatus = "Testing Calendar read access…";
        var started = _timeProvider.GetTimestamp();
        try
        {
            var events = await _calendarConnector
                .GetTodaysEventsAsync(cancellationToken)
                .ConfigureAwait(true);
            CalendarTestState = "Healthy";
            CalendarTestStatus =
                $"Connected · {ElapsedMilliseconds(started):F0} ms · {events.Count} event(s) today.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CalendarTestState = "Unknown";
            CalendarTestStatus = "Test canceled.";
        }
        catch (Exception exception)
        {
            CalendarTestState = "Unhealthy";
            CalendarTestStatus =
                $"{FormatTypedError(exception)} · {ElapsedMilliseconds(started):F0} ms";
        }
    }

    [RelayCommand]
    private async Task TestDropboxAsync(CancellationToken cancellationToken)
    {
        DropboxTestState = "Checking";
        DropboxTestStatus = "Testing Dropbox root access…";
        var started = _timeProvider.GetTimestamp();
        try
        {
            var items = await _dropboxConnector
                .ListAsync(string.Empty, cancellationToken)
                .ConfigureAwait(true);
            DropboxTestState = "Healthy";
            DropboxTestStatus =
                $"Connected · {ElapsedMilliseconds(started):F0} ms · {items.Count} root item(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DropboxTestState = "Unknown";
            DropboxTestStatus = "Test canceled.";
        }
        catch (Exception exception)
        {
            DropboxTestState = "Unhealthy";
            DropboxTestStatus =
                $"{FormatTypedError(exception)} · {ElapsedMilliseconds(started):F0} ms";
        }
    }

    private async Task<bool> SaveSecretReplacementAsync(
        string key,
        string replacement,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(replacement))
        {
            return false;
        }

        var value = replacement.Trim();
        await _secretStore
            .SetAsync(key, value, cancellationToken)
            .ConfigureAwait(true);
        _configuration[key] = value;
        return true;
    }

    private void ReloadConfiguration()
    {
        if (_configuration is IConfigurationRoot configurationRoot)
        {
            configurationRoot.Reload();
        }
    }

    private static bool IsMissingOrPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

    private static string FormatTypedError(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";

    private double ElapsedMilliseconds(long startedTimestamp) =>
        _timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds;

    private async Task ReportAsync(Exception exception, string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }
}
