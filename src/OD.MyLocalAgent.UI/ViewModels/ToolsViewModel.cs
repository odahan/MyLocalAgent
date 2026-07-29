using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Manages Google authorization, Dropbox secrets, and connector health checks.
/// </summary>
public sealed partial class ToolsViewModel :
    ObservableObject,
    IActivatablePage
{
    private readonly IGoogleCredentialProvider _googleCredentialProvider;
    private readonly IGmailConnector _gmailConnector;
    private readonly ICalendarConnector _calendarConnector;
    private readonly IDropboxConnector _dropboxConnector;
    private readonly ISecretStore _secretStore;
    private readonly IConfiguration _configuration;
    private readonly IIncidentReporter _incidentReporter;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly IMessenger _messenger;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolsViewModel"/> class.
    /// </summary>
    public ToolsViewModel(
        IGoogleCredentialProvider googleCredentialProvider,
        IGmailConnector gmailConnector,
        ICalendarConnector calendarConnector,
        IDropboxConnector dropboxConnector,
        ISecretStore secretStore,
        IConfiguration configuration,
        IIncidentReporter incidentReporter,
        IDesktopInteractionService desktopInteraction,
        IMessenger messenger)
    {
        _googleCredentialProvider = googleCredentialProvider;
        _gmailConnector = gmailConnector;
        _calendarConnector = calendarConnector;
        _dropboxConnector = dropboxConnector;
        _secretStore = secretStore;
        _configuration = configuration;
        _incidentReporter = incidentReporter;
        _desktopInteraction = desktopInteraction;
        _messenger = messenger;
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
    private async Task TestGmailAsync(CancellationToken cancellationToken)
    {
        GmailTestState = "Checking";
        GmailTestStatus = "Testing Gmail read access…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var messages = await _gmailConnector
                .GetUnreadMessagesAsync(cancellationToken)
                .ConfigureAwait(true);
            stopwatch.Stop();
            GmailTestState = "Healthy";
            GmailTestStatus =
                $"Connected · {stopwatch.ElapsedMilliseconds} ms · {messages.Count} unread message(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            GmailTestState = "Unknown";
            GmailTestStatus = "Test canceled.";
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            GmailTestState = "Unhealthy";
            GmailTestStatus =
                $"{FormatTypedError(exception)} · {stopwatch.ElapsedMilliseconds} ms";
        }
    }

    [RelayCommand]
    private async Task TestCalendarAsync(CancellationToken cancellationToken)
    {
        CalendarTestState = "Checking";
        CalendarTestStatus = "Testing Calendar read access…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var events = await _calendarConnector
                .GetTodaysEventsAsync(cancellationToken)
                .ConfigureAwait(true);
            stopwatch.Stop();
            CalendarTestState = "Healthy";
            CalendarTestStatus =
                $"Connected · {stopwatch.ElapsedMilliseconds} ms · {events.Count} event(s) today.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CalendarTestState = "Unknown";
            CalendarTestStatus = "Test canceled.";
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            CalendarTestState = "Unhealthy";
            CalendarTestStatus =
                $"{FormatTypedError(exception)} · {stopwatch.ElapsedMilliseconds} ms";
        }
    }

    [RelayCommand]
    private async Task TestDropboxAsync(CancellationToken cancellationToken)
    {
        DropboxTestState = "Checking";
        DropboxTestStatus = "Testing Dropbox root access…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var items = await _dropboxConnector
                .ListAsync(string.Empty, cancellationToken)
                .ConfigureAwait(true);
            stopwatch.Stop();
            DropboxTestState = "Healthy";
            DropboxTestStatus =
                $"Connected · {stopwatch.ElapsedMilliseconds} ms · {items.Count} root item(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DropboxTestState = "Unknown";
            DropboxTestStatus = "Test canceled.";
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            DropboxTestState = "Unhealthy";
            DropboxTestStatus =
                $"{FormatTypedError(exception)} · {stopwatch.ElapsedMilliseconds} ms";
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

    private async Task ReportAsync(Exception exception, string source)
    {
        _ = await _incidentReporter
            .ReportAsync(exception, source, CancellationToken.None)
            .ConfigureAwait(true);
    }
}
