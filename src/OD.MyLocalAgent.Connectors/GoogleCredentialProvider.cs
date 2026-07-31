using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Acquires Google OAuth credentials and stores tokens under the current user's local application data.
/// </summary>
public sealed class GoogleCredentialProvider : IGoogleCredentialProvider
{
    private const string UserName = "default";
    private static readonly string[] Scopes =
    [
        GmailService.Scope.GmailReadonly,
        GmailService.Scope.GmailSend,
        CalendarService.Scope.Calendar,
    ];
    private static readonly IReadOnlyList<string> ReadOnlyScopes =
        Array.AsReadOnly(Scopes);

    private readonly IOptions<GoogleOptions> _options;
    private readonly GoogleHttpClientFactoryAdapter _googleHttpClientFactory;
    private readonly SemaphoreSlim _credentialLock = new(1, 1);
    private UserCredential? _cachedCredential;

    /// <summary>
    /// Gets the directory containing the current user's persisted Google authorization data.
    /// </summary>
    public static string CredentialStorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OD.MyLocalAgent",
            "Auth",
            "Google");

    /// <summary>
    /// Gets the OAuth scopes requested by the application.
    /// </summary>
    public static IReadOnlyList<string> RequiredScopes => ReadOnlyScopes;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleCredentialProvider"/> class.
    /// </summary>
    /// <param name="options">The Google OAuth client configuration.</param>
    /// <param name="httpMessageHandlerFactory">The managed HTTP handler factory.</param>
    public GoogleCredentialProvider(
        IOptions<GoogleOptions> options,
        IHttpMessageHandlerFactory httpMessageHandlerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpMessageHandlerFactory);
        _options = options;
        _googleHttpClientFactory =
            new GoogleHttpClientFactoryAdapter(httpMessageHandlerFactory);
    }

    /// <inheritdoc />
    public async Task<UserCredential> AuthorizeInteractiveAsync(CancellationToken cancellationToken)
    {
        var options = GetValidatedOptions();
        await _credentialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dataStore = CreateDataStore();
            var initializer = CreateFlowInitializer(options, dataStore);
            _cachedCredential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                initializer,
                Scopes,
                UserName,
                cancellationToken,
                dataStore).ConfigureAwait(false);
            return _cachedCredential;
        }
        finally
        {
            _credentialLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<UserCredential?> GetCredentialSilentAsync(CancellationToken cancellationToken)
    {
        var options = GetValidatedOptions();
        await _credentialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedCredential is not null)
            {
                return _cachedCredential;
            }

            var dataStore = CreateDataStore();
            var token = await dataStore.GetAsync<TokenResponse>(UserName).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (token is null)
            {
                return null;
            }

            var flow = new GoogleAuthorizationCodeFlow(CreateFlowInitializer(options, dataStore));
            var credential = new UserCredential(flow, UserName, token);
            if (token.IsStale &&
                !await credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            _cachedCredential = credential;
            return credential;
        }
        finally
        {
            _credentialLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _credentialLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cachedCredential = null;
            if (Directory.Exists(CredentialStorePath))
            {
                Directory.Delete(CredentialStorePath, recursive: true);
            }
        }
        finally
        {
            _credentialLock.Release();
        }
    }

    private GoogleOptions GetValidatedOptions()
    {
        GoogleOptions options;
        try
        {
            options = _options.Value;
        }
        catch (OptionsValidationException exception)
        {
            throw new ConnectorConfigurationException(
                $"Google connector configuration is invalid: {exception.Message}",
                exception);
        }

        var validation = ConnectorOptionsValidation.ValidateGoogle(options);
        return validation is null
            ? options
            : throw new ConnectorConfigurationException(validation);
    }

    private GoogleAuthorizationCodeFlow.Initializer CreateFlowInitializer(
        GoogleOptions options,
        IDataStore dataStore)
    {
        return new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
            },
            DataStore = dataStore,
            HttpClientFactory = _googleHttpClientFactory,
            Scopes = Scopes,
        };
    }

    private static FileDataStore CreateDataStore() =>
        new(CredentialStorePath, fullPath: true);
}
