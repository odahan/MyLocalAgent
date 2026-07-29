using Google;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using System.Net.Mail;
using System.Text;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides Gmail operations without initiating interactive OAuth consent.
/// </summary>
public sealed class GmailConnector : IGmailConnector
{
    private const string CurrentUser = "me";
    private readonly IGoogleCredentialProvider _credentialProvider;
    private readonly GoogleHttpClientFactoryAdapter _googleHttpClientFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GmailConnector"/> class.
    /// </summary>
    /// <param name="credentialProvider">The silent Google credential provider.</param>
    /// <param name="httpClientFactory">The managed HTTP client factory.</param>
    /// <param name="timeProvider">The time source used to identify today's messages.</param>
    public GmailConnector(
        IGoogleCredentialProvider credentialProvider,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(credentialProvider);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _credentialProvider = credentialProvider;
        _googleHttpClientFactory = new GoogleHttpClientFactoryAdapter(httpClientFactory);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailMessageDto>> GetTodaysMessagesAsync(
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();
        var start = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            0,
            0,
            0,
            now.Offset);
        return GetMessagesAsync(
            $"after:{start.ToUnixTimeSeconds()}",
            "GetToday'sMessages",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailMessageDto>> GetUnreadMessagesAsync(
        CancellationToken cancellationToken)
    {
        return GetMessagesAsync("is:unread", "GetUnreadMessages", cancellationToken);
    }

    /// <inheritdoc />
    public Task<EmailMessageDto> SendMessageAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        ValidateMessage(to, subject, body);
        return ExecuteApiOperationAsync(
            "SendMessage",
            async () =>
            {
                using var service = await CreateServiceAsync(cancellationToken).ConfigureAwait(false);
                var raw = CreateRawMessage(to, subject, body);
                var request = service.Users.Messages.Send(
                    new Message { Raw = raw },
                    CurrentUser);
                var sent = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(sent.Id))
                {
                    return GmailMessageMapper.Map(sent);
                }

                var getRequest = service.Users.Messages.Get(CurrentUser, sent.Id);
                getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                var complete = await getRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                return GmailMessageMapper.Map(complete);
            },
            cancellationToken);
    }

    private Task<IReadOnlyList<EmailMessageDto>> GetMessagesAsync(
        string query,
        string operation,
        CancellationToken cancellationToken)
    {
        return ExecuteApiOperationAsync(
            operation,
            async () =>
            {
                using var service = await CreateServiceAsync(cancellationToken).ConfigureAwait(false);
                var messages = new List<EmailMessageDto>();
                string? pageToken = null;

                do
                {
                    var listRequest = service.Users.Messages.List(CurrentUser);
                    listRequest.Q = query;
                    listRequest.PageToken = pageToken;
                    var page = await listRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var messageReference in page.Messages ?? [])
                    {
                        if (string.IsNullOrWhiteSpace(messageReference.Id))
                        {
                            continue;
                        }

                        var getRequest = service.Users.Messages.Get(CurrentUser, messageReference.Id);
                        getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                        var message = await getRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                        messages.Add(GmailMessageMapper.Map(message));
                    }

                    pageToken = page.NextPageToken;
                }
                while (!string.IsNullOrWhiteSpace(pageToken));

                return (IReadOnlyList<EmailMessageDto>)messages;
            },
            cancellationToken);
    }

    private async Task<GmailService> CreateServiceAsync(CancellationToken cancellationToken)
    {
        var credential = await _credentialProvider
            .GetCredentialSilentAsync(cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            throw new ConnectorConfigurationException(
                "Google consent is required. Use the interactive Connect action before running Gmail operations.");
        }

        return new GmailService(new BaseClientService.Initializer
        {
            ApplicationName = "OD.MyLocalAgent",
            HttpClientFactory = _googleHttpClientFactory,
            HttpClientInitializer = credential,
        });
    }

    private static async Task<T> ExecuteApiOperationAsync<T>(
        string operation,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ConnectorConfigurationException)
        {
            throw;
        }
        catch (GoogleApiException exception)
        {
            var statusCode = (int)exception.HttpStatusCode;
            throw new GmailApiException(
                operation,
                statusCode == 0 ? null : statusCode,
                $"Gmail operation '{operation}' failed: {exception.Message}",
                exception);
        }
        catch (Exception exception)
        {
            throw new GmailApiException(
                operation,
                null,
                $"Gmail operation '{operation}' failed: {exception.Message}",
                exception);
        }
    }

    private static string CreateRawMessage(string to, string subject, string body)
    {
        var message = string.Join(
            "\r\n",
            $"To: {to}",
            $"Subject: {subject}",
            "MIME-Version: 1.0",
            "Content-Type: text/plain; charset=utf-8",
            "Content-Transfer-Encoding: 8bit",
            string.Empty,
            body);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(message))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void ValidateMessage(string to, string subject, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);
        _ = new MailAddress(to);
        if (subject.Contains('\r') || subject.Contains('\n'))
        {
            throw new ArgumentException("The subject cannot contain line breaks.", nameof(subject));
        }
    }
}
